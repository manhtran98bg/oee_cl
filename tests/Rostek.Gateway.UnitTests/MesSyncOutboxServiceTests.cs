using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Domain.Enums;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class MesSyncOutboxServiceTests
{
    [Fact]
    public async Task Enqueue_creates_outbox_without_production_context_when_context_is_not_required()
    {
        var previous = Raw("M16-01", 10, shotOkTotal: 100);
        var current = Raw("M16-01", 15, shotOkTotal: 108);
        var rawRepository = new FakeOeeRawIntervalRepository([previous]);
        var outboxRepository = new FakeMesSyncOutboxRepository();
        var service = CreateService([current], rawRepository, outboxRepository, requireProductionContext: false);

        var enqueued = await service.EnqueueSecondlyMetricsAsync("GW-M16-01", CancellationToken.None);

        Assert.Equal(1, enqueued);
        var message = Assert.Single(outboxRepository.Messages);
        Assert.Contains("\"data\"", message.PayloadJson);
        Assert.Contains("\"machine\":\"M16-01\"", message.PayloadJson);
        Assert.Contains("\"tag\":\"TEST\"", message.PayloadJson);
        Assert.Contains("\"session_id\":\"TEST_SESSION\"", message.PayloadJson);
    }

    [Fact]
    public async Task Enqueue_skips_outbox_without_production_context_when_context_is_required()
    {
        var previous = Raw("M16-01", 10, shotOkTotal: 100);
        var current = Raw("M16-01", 15, shotOkTotal: 108);
        var rawRepository = new FakeOeeRawIntervalRepository([previous]);
        var outboxRepository = new FakeMesSyncOutboxRepository();
        var service = CreateService([current], rawRepository, outboxRepository, requireProductionContext: true);

        var enqueued = await service.EnqueueSecondlyMetricsAsync("GW-M16-01", CancellationToken.None);

        Assert.Equal(0, enqueued);
        Assert.Empty(outboxRepository.Messages);
    }

    private static MesSyncOutboxService CreateService(
        IReadOnlyList<PlcRawInterval> currentRawIntervals,
        FakeOeeRawIntervalRepository rawRepository,
        FakeMesSyncOutboxRepository outboxRepository,
        bool requireProductionContext)
    {
        var options = Options.Create(new MesSyncOptions
        {
            SyncIntervalMs = 5000,
            RequireProductionContext = requireProductionContext
        });
        var rawIntervalService = new FakeOeeRawIntervalService(currentRawIntervals);
        var metricBuilder = new OeeMetricBuilder(rawRepository, NullLogger<OeeMetricBuilder>.Instance);
        return new MesSyncOutboxService(
            options,
            rawIntervalService,
            metricBuilder,
            outboxRepository,
            NullLogger<MesSyncOutboxService>.Instance);
    }

    private static PlcRawInterval Raw(string machineCode, long readAtUnixTimeSeconds, long? shotOkTotal = null) =>
        new()
        {
            MachineCode = machineCode,
            ProductionOrderCode = OeeTestProductionContext.ProductionOrderCode,
            SessionId = OeeTestProductionContext.SessionId,
            ReadAtUnixTimeSeconds = readAtUnixTimeSeconds,
            ShotOkTotal = shotOkTotal,
            CreatedUnixTimeSeconds = readAtUnixTimeSeconds
        };

    private sealed class FakeOeeRawIntervalService(IReadOnlyList<PlcRawInterval> rawIntervals) : IOeeRawIntervalService
    {
        public Task<IReadOnlyList<PlcRawInterval>> CaptureAsync(
            TimeSpan interval,
            IReadOnlyDictionary<string, ProductionContext> productionContexts,
            bool requireProductionContext,
            CancellationToken cancellationToken) =>
            Task.FromResult(rawIntervals);
    }

    private sealed class FakeOeeRawIntervalRepository(IReadOnlyList<PlcRawInterval> rawIntervals) : IOeeRawIntervalRepository
    {
        public Task<IReadOnlyList<PlcRawInterval>> InsertMissingAsync(IReadOnlyCollection<PlcRawInterval> rawIntervalsToInsert, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlcRawInterval>>(rawIntervalsToInsert.ToList());

        public Task<PlcRawInterval?> GetPreviousInContextAsync(
            string machineCode,
            string productionOrderCode,
            string sessionId,
            long contextStartedUnixTimeSeconds,
            long beforeReadAtUnixTimeSeconds,
            CancellationToken cancellationToken) =>
            Task.FromResult(rawIntervals
                .Where(raw => raw.MachineCode.Equals(machineCode, StringComparison.OrdinalIgnoreCase) &&
                              raw.ProductionOrderCode.Equals(productionOrderCode, StringComparison.OrdinalIgnoreCase) &&
                              raw.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase) &&
                              raw.ReadAtUnixTimeSeconds >= contextStartedUnixTimeSeconds &&
                              raw.ReadAtUnixTimeSeconds < beforeReadAtUnixTimeSeconds)
                .OrderByDescending(raw => raw.ReadAtUnixTimeSeconds)
                .FirstOrDefault());
    }

    private sealed class FakeMesSyncOutboxRepository : IMesSyncOutboxRepository
    {
        public List<MesSyncOutboxMessage> Messages { get; } = [];

        public Task<ProductionContext?> GetProductionContextAsync(string machineCode, CancellationToken cancellationToken) =>
            Task.FromResult<ProductionContext?>(null);

        public Task<IReadOnlyDictionary<string, ProductionContext>> ListActiveProductionContextsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, ProductionContext>>(new Dictionary<string, ProductionContext>(StringComparer.OrdinalIgnoreCase));

        public Task SaveProductionContextAsync(ProductionContext context, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task AddOutboxMessageAsync(MesSyncOutboxMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }

        public Task<List<MesSyncOutboxMessage>> TakePendingAsync(long nowUnixTimeSeconds, int batchSize, CancellationToken cancellationToken) =>
            Task.FromResult(new List<MesSyncOutboxMessage>());

        public Task MarkSyncedAsync(long id, long nowUnixTimeSeconds, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task MarkFailedAsync(long id, string error, long nextAttemptUnixTimeSeconds, long nowUnixTimeSeconds, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<MesSyncStatusDto> GetStatusAsync(bool enabled, CancellationToken cancellationToken) =>
            Task.FromResult(new MesSyncStatusDto(enabled, 0, 0, null, null));
    }
}
