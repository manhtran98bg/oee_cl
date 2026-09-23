using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.UnitTests.Support;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class SyncOutboxTests
{
    [Fact]
    public async Task Local_processing_enqueues_machine_state_event_and_realtime_snapshot()
    {
        var repository = new InMemoryOeeLocalRepository();
        var context = await repository.EnsureTestProductionContextAsync("M16-01", 100, CancellationToken.None);
        context.BaselineRawId = "baseline";
        context.BaselineCapturedAt = 100;
        await repository.SaveProductionContextAsync(context, CancellationToken.None);
        var cache = LoadedCache(repository);
        var processor = new OeeLocalProcessingService(
            Options.Create(new MesSyncOptions()),
            repository,
            new RealtimeSnapshotBuilder(
                repository,
                cache,
                new OrderQuantityCache(),
                new TestRuntimeConfigurationProvider(("M16-01", true))),
            new MachineStateEventBuilder(
                repository,
                cache,
                Options.Create(new MesSyncOptions { MachineStateEventGapThresholdMs = 15000 }),
                NullLogger<MachineStateEventBuilder>.Instance),
            new ProductionMetricBuilder(
                repository,
                cache,
                Options.Create(new MesSyncOptions()),
                NullLogger<ProductionMetricBuilder>.Instance),
            NullLogger<OeeLocalProcessingService>.Instance);

        await processor.ProcessAsync("GW-M16-01", [Raw(105)], 105, CancellationToken.None);

        Assert.Contains(repository.SyncOutboxMessages, item => item.Topic == SyncOutboxTopics.MachineStateEvent);
        Assert.Contains(repository.SyncOutboxMessages, item => item.Topic == SyncOutboxTopics.RealtimeSnapshot);
        Assert.Contains(repository.SyncOutboxMessages, item => item.Topic == SyncOutboxTopics.ProductionMetric);
    }

    [Fact]
    public async Task Local_processing_respects_disabled_sync_topics()
    {
        var repository = new InMemoryOeeLocalRepository();
        var context = await repository.EnsureTestProductionContextAsync("M16-01", 100, CancellationToken.None);
        context.BaselineRawId = "baseline";
        context.BaselineCapturedAt = 100;
        await repository.SaveProductionContextAsync(context, CancellationToken.None);
        var cache = LoadedCache(repository);
        var processor = new OeeLocalProcessingService(
            Options.Create(new MesSyncOptions
            {
                RealtimeSnapshotsEnabled = false,
                MachineStateEventsEnabled = false,
                ProductionMetricsEnabled = false
            }),
            repository,
            new RealtimeSnapshotBuilder(
                repository,
                cache,
                new OrderQuantityCache(),
                new TestRuntimeConfigurationProvider(("M16-01", true))),
            new MachineStateEventBuilder(
                repository,
                cache,
                Options.Create(new MesSyncOptions { MachineStateEventGapThresholdMs = 15000 }),
                NullLogger<MachineStateEventBuilder>.Instance),
            new ProductionMetricBuilder(
                repository,
                cache,
                Options.Create(new MesSyncOptions { ProductionMetricsEnabled = false }),
                NullLogger<ProductionMetricBuilder>.Instance),
            NullLogger<OeeLocalProcessingService>.Instance);

        await processor.ProcessAsync("GW-M16-01", [Raw(105)], 105, CancellationToken.None);

        Assert.NotEmpty(repository.MachineStateEvents);
        Assert.Empty(repository.SyncOutboxMessages);
    }

    [Fact]
    public async Task Local_processing_removes_stale_no_context_message_when_session_starts()
    {
        var repository = new InMemoryOeeLocalRepository();
        await repository.UpsertSyncOutboxMessageAsync(
            RealtimeMessage("realtime_snapshot:M16-01:no-context"),
            CancellationToken.None);
        var context = await repository.EnsureTestProductionContextAsync("M16-01", 100, CancellationToken.None);
        context.BaselineRawId = "baseline";
        context.BaselineCapturedAt = 100;
        await repository.SaveProductionContextAsync(context, CancellationToken.None);
        var processor = CreateProcessor(repository, LoadedCache(repository));

        await processor.ProcessAsync("GW-M16-01", [Raw(105)], 105, CancellationToken.None);

        Assert.DoesNotContain(repository.SyncOutboxMessages, message =>
            message.DedupeKey == "realtime_snapshot:M16-01:no-context");
        Assert.Contains(repository.SyncOutboxMessages, message =>
            message.DedupeKey == $"realtime_snapshot:M16-01:{context.OrderId}:{context.SessionId}");
    }

    [Fact]
    public async Task Local_processing_removes_stale_session_message_when_machine_has_no_context()
    {
        var repository = new InMemoryOeeLocalRepository();
        await repository.UpsertSyncOutboxMessageAsync(
            RealtimeMessage("realtime_snapshot:M16-01:ORDER-1:SESSION-1"),
            CancellationToken.None);
        var processor = CreateProcessor(repository, new ProductionContextCache());

        await processor.ProcessAsync("GW-M16-01", [Raw(105)], 105, CancellationToken.None);

        Assert.DoesNotContain(repository.SyncOutboxMessages, message =>
            message.DedupeKey == "realtime_snapshot:M16-01:ORDER-1:SESSION-1");
        Assert.Contains(repository.SyncOutboxMessages, message =>
            message.DedupeKey == "realtime_snapshot:M16-01:no-context");
    }

    [Fact]
    public async Task Dispatcher_marks_messages_synced_when_http_succeeds()
    {
        var repository = new InMemoryOeeLocalRepository();
        await repository.UpsertSyncOutboxMessageAsync(Message(), CancellationToken.None);
        var httpClient = new RecordingOutboxHttpClient();
        var dispatcher = new MesSyncOutboxDispatcher(
            Options.Create(new MesSyncOptions { BatchSize = 100 }),
            repository,
            httpClient,
            NullLogger<MesSyncOutboxDispatcher>.Instance);

        var result = await dispatcher.DispatchPendingAsync("GW-M16-01", [SyncOutboxTopics.MachineStateEvent], CancellationToken.None);

        Assert.Equal(1, result.SentCount);
        Assert.Equal(SyncOutboxStatuses.Synced, repository.SyncOutboxMessages.Single().Status);
        using var json = JsonDocument.Parse(httpClient.PayloadJson!);
        Assert.Equal(1, json.RootElement.GetProperty("schema_version").GetInt32());
        Assert.Equal("GW-M16-01", json.RootElement.GetProperty("gateway_id").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Dispatcher_uses_schema_version_two_for_realtime_snapshots()
    {
        var repository = new InMemoryOeeLocalRepository();
        await repository.UpsertSyncOutboxMessageAsync(
            RealtimeMessage("realtime_snapshot:M16-01:no-context"),
            CancellationToken.None);
        var httpClient = new RecordingOutboxHttpClient();
        var dispatcher = new MesSyncOutboxDispatcher(
            Options.Create(new MesSyncOptions { BatchSize = 100 }),
            repository,
            httpClient,
            NullLogger<MesSyncOutboxDispatcher>.Instance);

        await dispatcher.DispatchPendingAsync("GW-M16-01", [SyncOutboxTopics.RealtimeSnapshot], CancellationToken.None);

        using var json = JsonDocument.Parse(httpClient.PayloadJson!);
        Assert.Equal(2, json.RootElement.GetProperty("schema_version").GetInt32());
    }

    [Fact]
    public async Task Dispatcher_marks_messages_failed_when_http_fails()
    {
        var repository = new InMemoryOeeLocalRepository();
        await repository.UpsertSyncOutboxMessageAsync(Message(), CancellationToken.None);
        var dispatcher = new MesSyncOutboxDispatcher(
            Options.Create(new MesSyncOptions { BatchSize = 100, RetryCount = 3 }),
            repository,
            new FailingOutboxHttpClient(),
            NullLogger<MesSyncOutboxDispatcher>.Instance);

        var result = await dispatcher.DispatchPendingAsync("GW-M16-01", [SyncOutboxTopics.MachineStateEvent], CancellationToken.None);

        Assert.Equal(1, result.FailedCount);
        var message = repository.SyncOutboxMessages.Single();
        Assert.Equal(SyncOutboxStatuses.Failed, message.Status);
        Assert.Equal(1, message.AttemptCount);
        Assert.Contains("fail", message.LastError, StringComparison.OrdinalIgnoreCase);
    }

    private static ProductionContextCache LoadedCache(InMemoryOeeLocalRepository repository)
    {
        var cache = new ProductionContextCache();
        cache.Replace(repository.ListProductionContextsAsync(CancellationToken.None).GetAwaiter().GetResult());
        return cache;
    }

    private static OeeLocalProcessingService CreateProcessor(
        InMemoryOeeLocalRepository repository,
        ProductionContextCache cache) =>
        new(
            Options.Create(new MesSyncOptions()),
            repository,
            new RealtimeSnapshotBuilder(
                repository,
                cache,
                new OrderQuantityCache(),
                new TestRuntimeConfigurationProvider(("M16-01", true))),
            new MachineStateEventBuilder(
                repository,
                cache,
                Options.Create(new MesSyncOptions()),
                NullLogger<MachineStateEventBuilder>.Instance),
            new ProductionMetricBuilder(
                repository,
                cache,
                Options.Create(new MesSyncOptions()),
                NullLogger<ProductionMetricBuilder>.Instance),
            NullLogger<OeeLocalProcessingService>.Instance);

    private static PlcRawInterval Raw(long readAt) =>
        new()
        {
            Machine = "M16-01",
            ReadAt = readAt,
            PlcPeriodIndex = 1,
            RunState = OeeRunStates.Run,
            PeriodActive = 1,
            ShotOkTotal = 10,
            ShotNgTotal = 1,
            RunTimeTotalSec = 5,
            CycleTimeMs = 1000
        };

    private static SyncOutboxMessage Message() =>
        new()
        {
            Topic = SyncOutboxTopics.MachineStateEvent,
            DedupeKey = "machine_state_event:event-1",
            EndpointPath = MesSyncEndpointPaths.MachineStateEvents,
            PayloadJson = """{"event_id":"event-1","machine_code":"M16-01"}""",
            Status = SyncOutboxStatuses.Pending,
            NextAttemptAt = 0,
            CreatedAt = 1,
            UpdatedAt = 1
        };

    private static SyncOutboxMessage RealtimeMessage(string dedupeKey) =>
        new()
        {
            Topic = SyncOutboxTopics.RealtimeSnapshot,
            DedupeKey = dedupeKey,
            EndpointPath = MesSyncEndpointPaths.RealtimeSnapshots,
            PayloadJson = """{"machine_code":"M16-01","order_id":null,"session_id":null}""",
            Status = SyncOutboxStatuses.Pending,
            NextAttemptAt = 0,
            CreatedAt = 1,
            UpdatedAt = 1
        };

    private sealed class RecordingOutboxHttpClient : ISyncOutboxHttpClient
    {
        public string? PayloadJson { get; private set; }

        public Task SendAsync(string endpointPath, string payloadJson, CancellationToken cancellationToken)
        {
            PayloadJson = payloadJson;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingOutboxHttpClient : ISyncOutboxHttpClient
    {
        public Task SendAsync(string endpointPath, string payloadJson, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("fail");
    }
}
