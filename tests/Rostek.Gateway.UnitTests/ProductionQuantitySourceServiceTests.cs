using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.UnitTests.Support;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class ProductionQuantitySourceServiceTests
{
    [Fact]
    public async Task Oee_to_oi_accumulates_current_segment_and_is_idempotent()
    {
        var repository = RepositoryWithActiveContext(QuantityUpdateSources.Oee);
        repository.RawIntervals.Add(Raw(120, 20, 3, 18, 4, 2));
        repository.ProductionMetrics.Add(Metric(isFinal: false));
        repository.SyncOutboxMessages.Add(Outbox());
        var cache = LoadedCache(repository);
        var service = CreateService(repository, cache);
        var request = Request("EVENT-001", QuantityUpdateSources.Oi);

        var first = await service.HandleAsync(request, CancellationToken.None);
        var replay = await service.HandleAsync(request, CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.True(replay.Accepted);
        Assert.Single(repository.ProductionQuantitySourceEvents);
        Assert.Empty(repository.ProductionMetrics);
        Assert.Empty(repository.SyncOutboxMessages);
        var product = Product(repository.Contexts.Single());
        Assert.Equal(QuantityUpdateSources.Oi, product.QuantityUpdateSource);
        Assert.Equal(10, product.OeeAccumulatedShotOk);
        Assert.Equal(2, product.OeeAccumulatedShotNg);
        Assert.Equal(13, product.OeeAccumulatedRunTimeSec);
        Assert.Equal(3, product.OeeAccumulatedStopTimeSec);
        Assert.Equal(2, product.OeeAccumulatedErrorTimeSec);
        Assert.True(product.OeeAccumulatedProductionTimeSec > 0);
        Assert.Equal(QuantityUpdateSources.Oi, Product(cache.GetActive("M16-01", "ORDER-001")!).QuantityUpdateSource);
    }

    [Fact]
    public async Task Oi_to_oee_seeds_latest_raw_as_new_baseline()
    {
        var repository = RepositoryWithActiveContext(QuantityUpdateSources.Oi);
        repository.RawIntervals.Add(Raw(150, 40, 5, 30, 8, 3));
        var service = CreateService(repository, LoadedCache(repository));

        var response = await service.HandleAsync(Request("EVENT-002", QuantityUpdateSources.Oee), CancellationToken.None);

        Assert.True(response.Accepted);
        var product = Product(repository.Contexts.Single());
        Assert.Equal(QuantityUpdateSources.Oee, product.QuantityUpdateSource);
        Assert.Equal(40, product.OeeBaselineShotOkTotal);
        Assert.Equal(5, product.OeeBaselineShotNgTotal);
        Assert.Equal(30, product.OeeBaselineRunTimeTotalSec);
        Assert.Equal(8, product.OeeBaselineStopTimeTotalSec);
        Assert.Equal(3, product.OeeBaselineErrorTimeTotalSec);
        Assert.NotNull(product.OeeSegmentStartedAt);
    }

    [Fact]
    public async Task Reusing_event_code_with_different_payload_is_rejected()
    {
        var repository = RepositoryWithActiveContext(QuantityUpdateSources.Oee);
        var service = CreateService(repository, LoadedCache(repository));
        await service.HandleAsync(Request("EVENT-003", QuantityUpdateSources.Oi), CancellationToken.None);

        var response = await service.HandleAsync(Request("EVENT-003", QuantityUpdateSources.Oee), CancellationToken.None);

        Assert.False(response.Accepted);
        Assert.Equal("event_code_conflict", response.Error?.Code);
        Assert.Single(repository.ProductionQuantitySourceEvents);
    }

    [Fact]
    public async Task Oi_context_emits_null_realtime_metrics_and_no_production_metric()
    {
        var repository = RepositoryWithActiveContext(QuantityUpdateSources.Oi);
        var cache = LoadedCache(repository);
        var raw = Raw(120, 20, 3, 18, 4, 2);
        repository.RawIntervals.Add(raw);
        var realtimeBuilder = new RealtimeSnapshotBuilder(
            repository,
            cache,
            new OrderQuantityCache(),
            new TestRuntimeConfigurationProvider(("M16-01", true)));
        var metricBuilder = new ProductionMetricBuilder(
            repository,
            cache,
            Options.Create(new MesSyncOptions()),
            NullLogger<ProductionMetricBuilder>.Instance);

        var realtime = await realtimeBuilder.BuildAsync("GW-01", [raw], 120, CancellationToken.None);
        var metrics = await metricBuilder.BuildAsync("GW-01", [raw], 120, CancellationToken.None);

        var item = Assert.Single(realtime.Payload.Items);
        Assert.Equal(3, realtime.Payload.SchemaVersion);
        Assert.Null(item.ActualQty);
        Assert.Null(item.TotalQty);
        Assert.Null(item.PlannedQty);
        Assert.Null(item.Availability);
        Assert.Null(item.Performance);
        Assert.Null(item.Quality);
        Assert.Null(item.Oee);
        Assert.Empty(metrics.Metrics);
        Assert.Empty(repository.ProductionMetrics);
    }

    private static ProductionQuantitySourceService CreateService(
        InMemoryOeeLocalRepository repository,
        ProductionContextCache cache) =>
        new(
            repository,
            new InMemoryConfigRepository("M16-01"),
            cache,
            NullLogger<ProductionQuantitySourceService>.Instance,
            new OeeProcessingGate());

    private static InMemoryOeeLocalRepository RepositoryWithActiveContext(string source)
    {
        var repository = new InMemoryOeeLocalRepository();
        repository.Contexts.Add(new ProductionContext
        {
            SessionId = "SESSION-001",
            Machine = "M16-01",
            OrderId = "ORDER-001",
            ServerOrderId = "ORDER-001",
            Status = "active",
            ActivePeriodStartAt = 100,
            CurrentPlcPeriodIndex = 1,
            BaselineRawId = "RAW-BASELINE",
            BaselineCapturedAt = 100,
            BaselineShotOkTotal = 10,
            BaselineShotNgTotal = 1,
            BaselineRunTimeTotalSec = 5,
            BaselineStopTimeTotalSec = 1,
            BaselineErrorTimeTotalSec = 0,
            ProductsJson = $$"""[{"product_id":"PRODUCT-001","gain":1,"cycle_time":10,"target":100,"quantity_update_source":"{{source}}","oee_segment_started_at":100}]""",
            ExtraJson = "{}",
            UpdatedAt = 100
        });
        return repository;
    }

    private static ProductionQuantitySourceBatchRequest Request(string eventCode, string source) =>
        new()
        {
            SchemaVersion = 1,
            GatewayId = "GW-01",
            CreatedAt = 120,
            Items =
            [
                new ProductionQuantitySourceItemRequest
                {
                    EventCode = eventCode,
                    MachineCode = "M16-01",
                    OrderId = "ORDER-001",
                    ProductCode = "PRODUCT-001",
                    QuantityUpdateSource = source
                }
            ]
        };

    private static ProductionContextCache LoadedCache(InMemoryOeeLocalRepository repository)
    {
        var cache = new ProductionContextCache();
        cache.Replace(repository.Contexts);
        return cache;
    }

    private static OeeProductDefinition Product(ProductionContext context) =>
        Assert.Single(JsonSerializer.Deserialize<List<OeeProductDefinition>>(context.ProductsJson) ?? []);

    private static PlcRawInterval Raw(long readAt, long ok, long ng, long run, long stop, long error) =>
        new()
        {
            Id = $"RAW-{readAt}",
            Machine = "M16-01",
            ReadAt = readAt,
            PlcPeriodIndex = 1,
            PeriodActive = 1,
            RunState = OeeRunStates.Run,
            ShotOkTotal = ok,
            ShotNgTotal = ng,
            RunTimeTotalSec = run,
            StopTimeTotalSec = stop,
            ErrorTimeTotalSec = error,
            CycleTimeMs = 10000
        };

    private static ProductionMetric Metric(bool isFinal) =>
        new()
        {
            MetricId = "METRIC-001",
            BucketType = "session",
            Machine = "M16-01",
            OrderId = "ORDER-001",
            SessionId = "SESSION-001",
            IsFinal = isFinal
        };

    private static SyncOutboxMessage Outbox() =>
        new()
        {
            DedupeKey = "production_metric:METRIC-001",
            Topic = SyncOutboxTopics.ProductionMetric,
            EndpointPath = "/api/v1/gateway/oee/production-metrics",
            PayloadJson = "{\"machine_code\":\"M16-01\",\"order_id\":\"ORDER-001\"}",
            Status = SyncOutboxStatuses.Pending
        };
}
