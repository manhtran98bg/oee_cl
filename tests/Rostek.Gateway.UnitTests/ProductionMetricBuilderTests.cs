using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.UnitTests.Support;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class ProductionMetricBuilderTests
{
    [Fact]
    public async Task Build_creates_session_order_hour_and_day_metrics()
    {
        var repository = new InMemoryOeeLocalRepository();
        var context = Context();
        await repository.SaveProductionContextAsync(context, CancellationToken.None);
        repository.RawIntervals.Add(Raw(100, 100, 5, 10, 2, 1));
        var current = Raw(160, 150, 10, 70, 7, 3);
        repository.RawIntervals.Add(current);
        var builder = Builder(repository);

        var result = await builder.BuildAsync("GW-M16-01", [current], 160, CancellationToken.None);

        Assert.Equal(4, result.Metrics.Count);
        Assert.Contains(result.Metrics, item => item.BucketType == "session");
        Assert.Contains(result.Metrics, item => item.BucketType == "order");
        Assert.Contains(result.Metrics, item => item.BucketType == "hour");
        Assert.Contains(result.Metrics, item => item.BucketType == "day");

        var session = result.Metrics.Single(item => item.BucketType == "session");
        Assert.Equal("GW-M16-01:session:M16-01:LSX-001:SESSION-001:100", session.MetricId);
        Assert.Equal(55, session.ActualQty);
        Assert.Equal(55, session.TotalQty);
        Assert.Equal(12m, session.PlannedQty);
        Assert.Equal(1000, session.TargetQty);
        Assert.False(session.IsFinal);
        Assert.Equal(4, repository.ProductionMetrics.Count);
    }

    [Fact]
    public async Task Build_skips_metrics_when_counters_decrease()
    {
        var repository = new InMemoryOeeLocalRepository();
        var context = Context();
        await repository.SaveProductionContextAsync(context, CancellationToken.None);
        repository.RawIntervals.Add(Raw(100, 100, 5, 10, 0, 0));
        var current = Raw(160, 90, 5, 70, 0, 0);
        repository.RawIntervals.Add(current);
        var builder = Builder(repository);

        var result = await builder.BuildAsync("GW-M16-01", [current], 160, CancellationToken.None);

        Assert.Empty(result.Metrics);
        Assert.Empty(repository.ProductionMetrics);
    }

    private static ProductionMetricBuilder Builder(InMemoryOeeLocalRepository repository)
    {
        var cache = new ProductionContextCache();
        cache.Replace(repository.ListProductionContextsAsync(CancellationToken.None).GetAwaiter().GetResult());
        return new ProductionMetricBuilder(
            repository,
            cache,
            Options.Create(new MesSyncOptions { ProductionMetricTimeZoneId = "Asia/Ho_Chi_Minh" }),
            NullLogger<ProductionMetricBuilder>.Instance);
    }

    private static ProductionContext Context() =>
        new()
        {
            SessionId = "SESSION-001",
            Machine = "M16-01",
            Status = "active",
            OrderId = "LSX-001",
            ServerOrderId = "LSX-001",
            ActivePeriodStartAt = 100,
            CurrentPlcPeriodIndex = 1,
            ProductsJson = """[{"product_id":"SP-001","mold_code":"KHUON-001","gain":4,"cycle_time":16.0,"target":1000}]""",
            ExtraJson = "{}",
            BaselineRawId = "baseline",
            BaselineCapturedAt = 100,
            BaselineShotOkTotal = 100,
            BaselineShotNgTotal = 5,
            BaselineRunTimeTotalSec = 10,
            BaselineStopTimeTotalSec = 2,
            BaselineErrorTimeTotalSec = 1,
            UpdatedAt = 100
        };

    private static PlcRawInterval Raw(
        long readAt,
        long shotOk,
        long shotNg,
        long runTime,
        long stopTime,
        long errorTime) =>
        new()
        {
            Machine = "M16-01",
            ReadAt = readAt,
            PlcPeriodIndex = 1,
            RunState = OeeRunStates.Run,
            PeriodActive = 1,
            ShotOkTotal = shotOk,
            ShotNgTotal = shotNg,
            RunTimeTotalSec = runTime,
            StopTimeTotalSec = stopTime,
            ErrorTimeTotalSec = errorTime,
            CycleTimeMs = 1000
        };
}
