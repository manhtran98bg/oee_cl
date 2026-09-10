using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.UnitTests.Support;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class RealtimeSnapshotBuilderTests
{
    [Fact]
    public async Task Build_seeds_missing_context_baseline_and_skips_first_item()
    {
        var repository = new InMemoryOeeLocalRepository();
        var context = await repository.EnsureTestProductionContextAsync("M16-01", 100, CancellationToken.None);
        var cache = LoadedCache(repository);
        var builder = CreateBuilder(repository, cache);
        var raw = Raw(readAt: 105, shotOk: 10, shotNg: 2, runTime: 5, stopTime: 1, errorTime: 0);

        var result = await builder.BuildAsync("GW-M16-01", [raw], createdAt: 105, CancellationToken.None);

        Assert.Empty(result.Payload.Items);
        Assert.Equal(raw.Id, repository.Contexts.Single().BaselineRawId);
        Assert.Equal(105, repository.Contexts.Single().BaselineCapturedAt);
        Assert.Equal(10, repository.Contexts.Single().BaselineShotOkTotal);
    }

    [Fact]
    public async Task Build_calculates_realtime_snapshot_from_context_baseline()
    {
        var repository = new InMemoryOeeLocalRepository();
        var context = await repository.EnsureTestProductionContextAsync("M16-01", 100, CancellationToken.None);
        context.ProductsJson = """[{"product_id":"SP-001","mold_code":"KHUON-001","gain":4.0,"cycle_time":12.5,"target":10000}]""";
        context.BaselineRawId = "baseline";
        context.BaselineCapturedAt = 100;
        context.BaselineShotOkTotal = 100;
        context.BaselineShotNgTotal = 5;
        context.BaselineRunTimeTotalSec = 10;
        context.BaselineStopTimeTotalSec = 2;
        context.BaselineErrorTimeTotalSec = 1;
        await repository.SaveProductionContextAsync(context, CancellationToken.None);
        var builder = CreateBuilder(repository, LoadedCache(repository));
        var raw = Raw(readAt: 110, shotOk: 120, shotNg: 8, runTime: 18, stopTime: 4, errorTime: 2);

        var result = await builder.BuildAsync("GW-M16-01", [raw], createdAt: 160, CancellationToken.None);

        var item = Assert.Single(result.Payload.Items);
        Assert.Equal("M16-01", item.MachineCode);
        Assert.Equal("TEST_ORDER", item.OrderId);
        Assert.Equal("M16-01-TEST_SESSION", item.SessionId);
        Assert.Equal("SP-001", item.ProductCode);
        Assert.Equal("KHUON-001", item.MoldCode);
        Assert.Equal(23, item.ActualQty);
        Assert.Equal(4.8m, item.PlannedQty);
        Assert.Equal(13.333333m, item.Availability);
        Assert.Equal(100m, item.Performance);
        Assert.Equal(86.956522m, item.Quality);
        Assert.Equal(11.594203m, item.Oee);
    }

    [Fact]
    public async Task Build_creates_one_item_per_active_context_on_same_machine()
    {
        var repository = new InMemoryOeeLocalRepository();
        var first = await repository.EnsureTestProductionContextAsync("M16-01", 100, CancellationToken.None);
        first.OrderId = "MO-001";
        first.SessionId = "M16-01-MO-001-100";
        first.BaselineRawId = "baseline-1";
        first.BaselineCapturedAt = 100;
        first.ProductsJson = """[{"product_id":"SP-001","gain":1.0,"cycle_time":10.0,"target":0}]""";
        var second = new ProductionContext
        {
            SessionId = "M16-01-MO-002-100",
            Machine = "M16-01",
            Status = "active",
            OrderId = "MO-002",
            ServerOrderId = "MO-002",
            ActivePeriodStartAt = 100,
            CurrentPlcPeriodIndex = 1,
            ProductsJson = """[{"product_id":"SP-002","gain":1.0,"cycle_time":10.0,"target":0}]""",
            ExtraJson = "{}",
            BaselineRawId = "baseline-2",
            BaselineCapturedAt = 100
        };
        await repository.SaveProductionContextAsync(first, CancellationToken.None);
        await repository.SaveProductionContextAsync(second, CancellationToken.None);
        var builder = CreateBuilder(repository, LoadedCache(repository));

        var result = await builder.BuildAsync("GW-M16-01", [Raw(readAt: 110, shotOk: 10, shotNg: 1, runTime: 10, stopTime: 0, errorTime: 0)], createdAt: 110, CancellationToken.None);

        Assert.Equal(2, result.Payload.Items.Count);
        Assert.Contains(result.Payload.Items, item => item.OrderId == "MO-001" && item.ProductCode == "SP-001");
        Assert.Contains(result.Payload.Items, item => item.OrderId == "MO-002" && item.ProductCode == "SP-002");
    }

    [Fact]
    public async Task Sync_drops_batch_when_client_fails()
    {
        var raw = Raw(readAt: 110, shotOk: 120, shotNg: 8, runTime: 18, stopTime: 4, errorTime: 2);
        var capture = new StubRawDataCaptureService([raw]);
        var builder = new StubRealtimeSnapshotBuilder(new RealtimeSnapshotBatchPayload(
            1,
            "GW-M16-01",
            110,
            [new RealtimeSnapshotItemPayload("M16-01", "TEST_ORDER", "SESSION", "SP", null, "run", 1, 1, 10, 100, 100, 10, EmptyExtra())]));
        var status = new RealtimeSnapshotSyncStatusStore();
        var service = new RealtimeSnapshotSyncService(
            Options.Create(new MesSyncOptions { Enabled = true, BaseUrl = "http://localhost", SyncIntervalMs = 5000 }),
            capture,
            builder,
            new FailingRealtimeSnapshotClient(),
            status,
            NullLogger<RealtimeSnapshotSyncService>.Instance);

        await service.SyncAsync("GW-M16-01", CancellationToken.None);

        Assert.Equal(1, status.Current.DroppedBatchCount);
        Assert.Contains("fail", status.Current.LastError, StringComparison.OrdinalIgnoreCase);
    }

    private static RealtimeSnapshotBuilder CreateBuilder(InMemoryOeeLocalRepository repository, ProductionContextCache cache) =>
        new(repository, cache, NullLogger<RealtimeSnapshotBuilder>.Instance);

    private static ProductionContextCache LoadedCache(InMemoryOeeLocalRepository repository)
    {
        var cache = new ProductionContextCache();
        cache.Replace(repository.ListProductionContextsAsync(CancellationToken.None).GetAwaiter().GetResult());
        return cache;
    }

    private static PlcRawInterval Raw(long readAt, long shotOk, long shotNg, long runTime, long stopTime, long errorTime) =>
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
            CycleTimeMs = 12500
        };

    private static System.Text.Json.JsonElement EmptyExtra() =>
        System.Text.Json.JsonSerializer.SerializeToElement(new { });

    private sealed class StubRawDataCaptureService(IReadOnlyList<PlcRawInterval> rawIntervals) : IRawDataCaptureService
    {
        public Task<IReadOnlyList<PlcRawInterval>> CaptureAsync(TimeSpan interval, bool requireProductionContext, CancellationToken cancellationToken) =>
            Task.FromResult(rawIntervals);
    }

    private sealed class StubRealtimeSnapshotBuilder(RealtimeSnapshotBatchPayload payload) : IRealtimeSnapshotBuilder
    {
        public Task<RealtimeSnapshotBuildResult> BuildAsync(string gatewayId, IReadOnlyCollection<PlcRawInterval> rawIntervals, long createdAt, CancellationToken cancellationToken) =>
            Task.FromResult(new RealtimeSnapshotBuildResult(payload, rawIntervals.Count, 0));
    }

    private sealed class FailingRealtimeSnapshotClient : IRealtimeSnapshotClient
    {
        public Task SendAsync(RealtimeSnapshotBatchPayload payload, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("fail");
    }
}
