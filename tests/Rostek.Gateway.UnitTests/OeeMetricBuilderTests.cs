using Microsoft.Extensions.Logging.Abstractions;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.UnitTests.Fakes;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class OeeMetricBuilderTests
{
    [Fact]
    public async Task Current_raw_without_previous_raw_does_not_build_metric()
    {
        var repository = CreateRepository("M16-01", periodStartAt: 10);
        var builder = CreateBuilder(repository);

        var result = await builder.BuildMetricsAsync([Raw("M16-01", 10, shotOkTotal: 100)], CancellationToken.None);

        Assert.Empty(result.ProductionMetrics);
    }

    [Fact]
    public async Task Current_raw_with_previous_raw_builds_python_style_metrics()
    {
        var repository = CreateRepository("M16-01", periodStartAt: 10);
        repository.RawIntervals.Add(Raw("M16-01", 10, runState: "run", shotOkTotal: 100, shotNgTotal: 10, cycleTimeMs: 1000, runTimeTotalSec: 10, stopTimeTotalSec: 2, errorTimeTotalSec: 1));
        var builder = CreateBuilder(repository);

        var result = await builder.BuildMetricsAsync(
            [Raw("M16-01", 15, runState: "run", shotOkTotal: 108, shotNgTotal: 11, cycleTimeMs: 1000, runTimeTotalSec: 14, stopTimeTotalSec: 3, errorTimeTotalSec: 2)],
            CancellationToken.None);

        var second = Assert.Single(result.ProductionMetrics, metric => metric.MetricType == OeeMetricTypes.Second);
        Assert.Equal("M16-01", second.Machine);
        Assert.Equal("TEST_ORDER", second.OrderId);
        Assert.Equal("TEST_PRODUCT", second.ProductId);
        Assert.Equal(8, second.TotalQty);
        Assert.Equal(1, second.NgQty);
        Assert.Equal(4, second.RunTimeSec);
        Assert.Equal(1, second.StopTimeSec);
        Assert.Equal(1, second.ErrorTimeSec);
        Assert.Equal(6, second.ProdTimeSec);
        Assert.InRange(second.Availability, 66.66m, 66.67m);
        Assert.Equal(100m, second.Performance);
        Assert.InRange(second.Quality, 88.88m, 88.89m);
        Assert.InRange(second.Oee, 59.25m, 59.26m);
    }

    [Fact]
    public async Task State_metric_uses_timestamp_duration_not_plc_time_delta()
    {
        var repository = CreateRepository("M16-01", periodStartAt: 10);
        repository.RawIntervals.Add(Raw("M16-01", 10, runState: "stop", runTimeTotalSec: 100, stopTimeTotalSec: 20));
        var builder = CreateBuilder(repository);

        var result = await builder.BuildMetricsAsync(
            [Raw("M16-01", 15, runState: "stop", runTimeTotalSec: 105, stopTimeTotalSec: 21)],
            CancellationToken.None);

        var state = Assert.Single(result.ProductionMetrics, metric => metric.MetricType == OeeMetricTypes.State);
        Assert.Equal("stop", state.RunState);
        Assert.Equal(0, state.RunTimeSec);
        Assert.Equal(5, state.StopTimeSec);
        Assert.Equal(0, state.ErrorTimeSec);
    }

    [Fact]
    public async Task Performance_uses_actual_over_planned_with_cycle_time_from_product_json()
    {
        var repository = CreateRepository(
            "M16-01",
            periodStartAt: 10,
            productsJson: """[{"product_id":"TEST_PRODUCT","gain":1.0,"cycle_time":2.0,"target":0}]""");
        repository.RawIntervals.Add(Raw("M16-01", 10, shotOkTotal: 100, shotNgTotal: 10, runTimeTotalSec: 0));
        var builder = CreateBuilder(repository);

        var result = await builder.BuildMetricsAsync(
            [Raw("M16-01", 15, shotOkTotal: 104, shotNgTotal: 11, runTimeTotalSec: 10)],
            CancellationToken.None);

        var second = Assert.Single(result.ProductionMetrics, metric => metric.MetricType == OeeMetricTypes.Second);
        Assert.Equal(4, second.TotalQty);
        Assert.Equal(1, second.NgQty);
        Assert.Equal(10, second.ProdTimeSec);
        Assert.Equal(5m, second.PlanQty);
        Assert.Equal(100m, second.Performance);
    }

    [Fact]
    public async Task Counter_reset_produces_zero_delta_like_python()
    {
        var repository = CreateRepository("M16-01", periodStartAt: 10);
        repository.RawIntervals.Add(Raw("M16-01", 10, shotOkTotal: 100));
        var builder = CreateBuilder(repository);

        var result = await builder.BuildMetricsAsync([Raw("M16-01", 15, shotOkTotal: 90)], CancellationToken.None);

        var second = Assert.Single(result.ProductionMetrics, metric => metric.MetricType == OeeMetricTypes.Second);
        Assert.Equal(0, second.TotalQty);
    }

    [Fact]
    public async Task Raw_without_active_context_is_skipped()
    {
        var repository = new FakeOeeLocalRepository();
        repository.RawIntervals.Add(Raw("M16-01", 10, shotOkTotal: 100));
        var builder = new OeeMetricBuilder(repository, new ProductionContextStore(), NullLogger<OeeMetricBuilder>.Instance);

        var result = await builder.BuildMetricsAsync([Raw("M16-01", 15, shotOkTotal: 110)], CancellationToken.None);

        Assert.Empty(result.ProductionMetrics);
    }

    private static OeeMetricBuilder CreateBuilder(FakeOeeLocalRepository repository) =>
        new(repository, LoadedStore(repository), NullLogger<OeeMetricBuilder>.Instance);

    private static ProductionContextStore LoadedStore(FakeOeeLocalRepository repository)
    {
        var store = new ProductionContextStore();
        store.Replace(repository.Contexts.ToDictionary(context => context.Machine, StringComparer.OrdinalIgnoreCase));
        return store;
    }

    private static FakeOeeLocalRepository CreateRepository(string machine, long periodStartAt, string? productsJson = null)
    {
        var repository = new FakeOeeLocalRepository();
        var context = new ProductionContext
        {
            Machine = machine,
            Mode = OeeTestProductionContext.Mode,
            OrderId = OeeTestProductionContext.OrderId,
            ServerOrderId = OeeTestProductionContext.ServerOrderId,
            ActivePeriodId = $"{machine}-{OeeTestProductionContext.PeriodId}",
            CurrentPlcPeriodIndex = OeeTestProductionContext.PlcPeriodIndex,
            ProductsJson = productsJson ?? OeeTestProductionContext.ProductsJson,
            TagsJson = OeeTestProductionContext.TagsJson,
            ExtraJson = OeeTestProductionContext.ExtraJson,
            Status = "active",
            UpdatedAt = periodStartAt
        };
        repository.Contexts.Add(context);
        repository.Periods.Add(new ProductionPeriod
        {
            PeriodId = context.ActivePeriodId,
            Machine = machine,
            PlcPeriodIndex = context.CurrentPlcPeriodIndex,
            Mode = context.Mode,
            OrderId = context.OrderId,
            ServerOrderId = context.ServerOrderId,
            ProductsJson = context.ProductsJson,
            TagsJson = context.TagsJson,
            ExtraJson = context.ExtraJson,
            StartAt = periodStartAt,
            Status = "active",
            CreatedAt = periodStartAt,
            UpdatedAt = periodStartAt
        });
        return repository;
    }

    private static PlcRawInterval Raw(
        string machine,
        long readAt,
        string runState = "run",
        long shotOkTotal = 0,
        long shotNgTotal = 0,
        int cycleTimeMs = 1000,
        long runTimeTotalSec = 0,
        long stopTimeTotalSec = 0,
        long errorTimeTotalSec = 0) =>
        new()
        {
            Machine = machine,
            ReadAt = readAt,
            PlcPeriodIndex = OeeTestProductionContext.PlcPeriodIndex,
            RunState = runState,
            ShotOkTotal = shotOkTotal,
            ShotNgTotal = shotNgTotal,
            CycleTimeMs = cycleTimeMs,
            RunTimeTotalSec = runTimeTotalSec,
            StopTimeTotalSec = stopTimeTotalSec,
            ErrorTimeTotalSec = errorTimeTotalSec,
            PeriodActive = 1
        };
}
