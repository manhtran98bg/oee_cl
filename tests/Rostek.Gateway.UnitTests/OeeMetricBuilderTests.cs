using Microsoft.Extensions.Logging.Abstractions;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Domain.Enums;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class OeeMetricBuilderTests
{
    [Fact]
    public async Task Current_raw_without_previous_raw_does_not_build_metric()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new FakeOeeRawIntervalRepository();
        var builder = CreateBuilder(repository);

        var metrics = await builder.BuildMetricsAsync("GW-M16-01", [Raw("M16-01", now, shotOkTotal: 100)], Contexts("M16-01"), now.ToUnixTimeSeconds(), true, CancellationToken.None);

        Assert.Empty(metrics);
    }

    [Fact]
    public async Task Current_raw_with_previous_raw_builds_delta_metric()
    {
        var first = DateTimeOffset.UtcNow;
        var second = first.AddSeconds(5);
        var repository = new FakeOeeRawIntervalRepository();
        repository.RawIntervals.Add(Raw("M16-01", first, machineState: 1, shotOkTotal: 100, shotNgTotal: 10, cycleTimeMs: 1400, runTimeTotal: 10, stopTimeTotal: 2, errorTimeTotal: 1));
        var builder = CreateBuilder(repository);

        var metrics = await builder.BuildMetricsAsync(
            "GW-M16-01",
            [Raw("M16-01", second, machineState: 2, shotOkTotal: 108, shotNgTotal: 11, cycleTimeMs: 1500, runTimeTotal: 14, stopTimeTotal: 3, errorTimeTotal: 2)],
            Contexts("M16-01", first),
            second.ToUnixTimeSeconds(),
            true,
            CancellationToken.None);

        var metric = Assert.Single(metrics);
        Assert.Equal("M16-01", metric.Machine);
        Assert.Equal("MO-001", metric.OrderId);
        Assert.Equal("CMD-001", metric.Tag);
        Assert.Equal(9, metric.Total);
        Assert.Equal(1, metric.NgQty);
        Assert.Equal(4m, metric.RunTime);
        Assert.Equal(1m, metric.StopTime);
        Assert.Equal(1m, metric.ErrorTime);
        Assert.Equal(6m, metric.ProdTime);
        Assert.Equal(1.5m, metric.Cycle);
        Assert.Equal(0.666667m, metric.Availability);
        Assert.Equal(1m, metric.Performance);
        Assert.Equal(0.888889m, metric.Quality);
        Assert.Equal(0.592593m, metric.Oee);
    }

    [Fact]
    public async Task Counter_reset_produces_zero_delta_like_python()
    {
        var first = DateTimeOffset.UtcNow;
        var second = first.AddSeconds(5);
        var repository = new FakeOeeRawIntervalRepository();
        repository.RawIntervals.Add(Raw("M16-01", first, shotOkTotal: 100));
        var builder = CreateBuilder(repository);

        var metrics = await builder.BuildMetricsAsync(
            "GW-M16-01",
            [Raw("M16-01", second, shotOkTotal: 90)],
            Contexts("M16-01"),
            second.ToUnixTimeSeconds(),
            true,
            CancellationToken.None);

        Assert.Equal(0, Assert.Single(metrics).Total);
    }

    [Fact]
    public async Task Machine_without_active_context_is_skipped_when_context_is_required()
    {
        var first = DateTimeOffset.UtcNow;
        var second = first.AddSeconds(5);
        var repository = new FakeOeeRawIntervalRepository();
        repository.RawIntervals.Add(Raw("M16-01", first, shotOkTotal: 100));
        var builder = CreateBuilder(repository);

        var metrics = await builder.BuildMetricsAsync("GW-M16-01", [Raw("M16-01", second, shotOkTotal: 110)], new Dictionary<string, ProductionContext>(), second.ToUnixTimeSeconds(), true, CancellationToken.None);

        Assert.Empty(metrics);
    }

    [Fact]
    public async Task Machine_without_active_context_builds_test_metric_when_context_is_not_required()
    {
        var first = DateTimeOffset.UtcNow;
        var second = first.AddSeconds(5);
        var repository = new FakeOeeRawIntervalRepository();
        repository.RawIntervals.Add(Raw("M16-01", first, shotOkTotal: 100, shotNgTotal: 3, runTimeTotal: 1, productionOrderCode: OeeTestProductionContext.ProductionOrderCode, sessionId: OeeTestProductionContext.SessionId));
        var builder = CreateBuilder(repository);

        var metrics = await builder.BuildMetricsAsync(
            "GW-M16-01",
            [Raw("M16-01", second, shotOkTotal: 110, shotNgTotal: 4, runTimeTotal: 6, productionOrderCode: OeeTestProductionContext.ProductionOrderCode, sessionId: OeeTestProductionContext.SessionId)],
            new Dictionary<string, ProductionContext>(),
            second.ToUnixTimeSeconds(),
            false,
            CancellationToken.None);

        var metric = Assert.Single(metrics);
        Assert.Equal("Started", metric.Mode);
        Assert.Equal("M16-01", metric.Machine);
        Assert.Equal(OeeTestProductionContext.ProductionOrderCode, metric.OrderId);
        Assert.Equal(OeeTestProductionContext.SessionId, metric.SessionId);
        Assert.Equal("TEST", metric.Tag);
        Assert.Equal(0, metric.StartAt);
        Assert.Equal(second.ToUnixTimeSeconds(), metric.EndAt);
        Assert.Equal(11, metric.Total);
        Assert.Equal(1, metric.NgQty);
    }

    [Fact]
    public async Task Stopped_context_is_skipped_when_context_is_required()
    {
        var first = DateTimeOffset.UtcNow;
        var second = first.AddSeconds(5);
        var repository = new FakeOeeRawIntervalRepository();
        repository.RawIntervals.Add(Raw("M16-01", first, shotOkTotal: 100));
        var builder = CreateBuilder(repository);

        var contexts = Contexts("M16-01").ToDictionary();
        contexts["M16-01"].Status = ProductionContextStatus.Stopped;

        var metrics = await builder.BuildMetricsAsync(
            "GW-M16-01",
            [Raw("M16-01", second, shotOkTotal: 110)],
            contexts,
            second.ToUnixTimeSeconds(),
            true,
            CancellationToken.None);

        Assert.Empty(metrics);
    }

    [Fact]
    public async Task Previous_raw_from_different_session_is_not_used()
    {
        var first = DateTimeOffset.UtcNow;
        var second = first.AddSeconds(5);
        var repository = new FakeOeeRawIntervalRepository();
        repository.RawIntervals.Add(Raw("M16-01", first, shotOkTotal: 100, sessionId: "OLD-SESSION"));
        var builder = CreateBuilder(repository);

        var metrics = await builder.BuildMetricsAsync(
            "GW-M16-01",
            [Raw("M16-01", second, shotOkTotal: 110)],
            Contexts("M16-01"),
            second.ToUnixTimeSeconds(),
            true,
            CancellationToken.None);

        Assert.Empty(metrics);
    }

    [Fact]
    public async Task Non_adjacent_previous_raw_builds_delta_like_python()
    {
        var first = DateTimeOffset.UtcNow;
        var second = first.AddSeconds(565);
        var repository = new FakeOeeRawIntervalRepository();
        repository.RawIntervals.Add(Raw("M16-01", first, shotOkTotal: 100, runTimeTotal: 2107, productionOrderCode: OeeTestProductionContext.ProductionOrderCode, sessionId: OeeTestProductionContext.SessionId));
        var builder = CreateBuilder(repository);

        var metrics = await builder.BuildMetricsAsync(
            "GW-M16-01",
            [Raw("M16-01", second, shotOkTotal: 110, runTimeTotal: 2674, productionOrderCode: OeeTestProductionContext.ProductionOrderCode, sessionId: OeeTestProductionContext.SessionId)],
            new Dictionary<string, ProductionContext>(),
            second.ToUnixTimeSeconds(),
            false,
            CancellationToken.None);

        var metric = Assert.Single(metrics);
        Assert.Equal(10, metric.Total);
        Assert.Equal(567m, metric.RunTime);
    }

    private static OeeMetricBuilder CreateBuilder(FakeOeeRawIntervalRepository repository) =>
        new(repository, NullLogger<OeeMetricBuilder>.Instance);

    private static IReadOnlyDictionary<string, ProductionContext> Contexts(string machineCode, DateTimeOffset? startAt = null) =>
        new Dictionary<string, ProductionContext>(StringComparer.OrdinalIgnoreCase)
        {
            [machineCode] = new()
            {
                MachineCode = machineCode,
                CommandCode = "CMD-001",
                Status = ProductionContextStatus.Started,
                ProductionOrderCode = "MO-001",
                SessionId = "SESSION-001",
                StartedUnixTimeSeconds = startAt?.ToUnixTimeSeconds()
            }
        };

    private static PlcRawInterval Raw(
        string machineCode,
        DateTimeOffset readAtUtc,
        int? machineState = null,
        long? shotOkTotal = null,
        long? shotNgTotal = null,
        int? cycleTimeMs = null,
        long? runTimeTotal = null,
        long? stopTimeTotal = null,
        long? errorTimeTotal = null,
        string productionOrderCode = "MO-001",
        string sessionId = "SESSION-001") =>
        new()
        {
            MachineCode = machineCode,
            ProductionOrderCode = productionOrderCode,
            SessionId = sessionId,
            ReadAtUnixTimeSeconds = readAtUtc.ToUnixTimeSeconds(),
            MachineState = machineState,
            ShotOkTotal = shotOkTotal,
            ShotNgTotal = shotNgTotal,
            CycleTimeMs = cycleTimeMs,
            RunTimeTotal = runTimeTotal,
            StopTimeTotal = stopTimeTotal,
            ErrorTimeTotal = errorTimeTotal,
            CreatedUnixTimeSeconds = readAtUtc.ToUnixTimeSeconds()
        };

    private sealed class FakeOeeRawIntervalRepository : IOeeRawIntervalRepository
    {
        public List<PlcRawInterval> RawIntervals { get; } = [];

        public Task<IReadOnlyList<PlcRawInterval>> InsertMissingAsync(IReadOnlyCollection<PlcRawInterval> rawIntervals, CancellationToken cancellationToken)
        {
            var inserted = rawIntervals
                .Where(raw => !RawIntervals.Any(existing =>
                    existing.MachineCode.Equals(raw.MachineCode, StringComparison.OrdinalIgnoreCase) &&
                    existing.ReadAtUnixTimeSeconds == raw.ReadAtUnixTimeSeconds))
                .ToList();
            RawIntervals.AddRange(inserted);
            return Task.FromResult<IReadOnlyList<PlcRawInterval>>(inserted);
        }

        public Task<PlcRawInterval?> GetPreviousInContextAsync(
            string machineCode,
            string productionOrderCode,
            string sessionId,
            long contextStartedUnixTimeSeconds,
            long beforeReadAtUnixTimeSeconds,
            CancellationToken cancellationToken) =>
            Task.FromResult(RawIntervals
                .Where(raw =>
                    raw.MachineCode.Equals(machineCode, StringComparison.OrdinalIgnoreCase) &&
                    raw.ProductionOrderCode.Equals(productionOrderCode, StringComparison.OrdinalIgnoreCase) &&
                    raw.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase) &&
                    raw.ReadAtUnixTimeSeconds >= contextStartedUnixTimeSeconds &&
                    raw.ReadAtUnixTimeSeconds < beforeReadAtUnixTimeSeconds)
                .OrderByDescending(raw => raw.ReadAtUnixTimeSeconds)
                .FirstOrDefault());
    }
}
