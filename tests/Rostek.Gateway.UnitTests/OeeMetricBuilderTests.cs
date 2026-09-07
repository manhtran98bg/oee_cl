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

        var metrics = await builder.BuildMetricsAsync("GW-M16-01", [Raw("M16-01", now, shotOkTotal: 100)], Contexts("M16-01"), now.ToUnixTimeSeconds(), CancellationToken.None);

        Assert.Empty(metrics);
    }

    [Fact]
    public async Task Current_raw_with_previous_raw_builds_delta_metric()
    {
        var first = DateTimeOffset.UtcNow;
        var second = first.AddSeconds(5);
        var repository = new FakeOeeRawIntervalRepository();
        repository.RawIntervals.Add(Raw("M16-01", first, machineState: 1, shotOkTotal: 100, shotNgTotal: 10, cycleTimeMs: 1400, runTimeTotal: 10000, stopTimeTotal: 2000, errorTimeTotal: 500));
        var builder = CreateBuilder(repository);

        var metrics = await builder.BuildMetricsAsync(
            "GW-M16-01",
            [Raw("M16-01", second, machineState: 2, shotOkTotal: 108, shotNgTotal: 11, cycleTimeMs: 1500, runTimeTotal: 14000, stopTimeTotal: 2500, errorTimeTotal: 700)],
            Contexts("M16-01", first),
            second.ToUnixTimeSeconds(),
            CancellationToken.None);

        var metric = Assert.Single(metrics);
        Assert.Equal("M16-01", metric.Machine);
        Assert.Equal("MO-001", metric.OrderId);
        Assert.Equal("CMD-001", metric.Tag);
        Assert.Equal(9, metric.Total);
        Assert.Equal(1, metric.NgQty);
        Assert.Equal(4m, metric.RunTime);
        Assert.Equal(0.5m, metric.StopTime);
        Assert.Equal(0.2m, metric.ErrorTime);
        Assert.Equal(4.7m, metric.ProdTime);
        Assert.Equal(1.5m, metric.Cycle);
        Assert.Equal(0.851064m, metric.Availability);
        Assert.Equal(1m, metric.Performance);
        Assert.Equal(0.888889m, metric.Quality);
        Assert.Equal(0.756501m, metric.Oee);
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
            CancellationToken.None);

        Assert.Equal(0, Assert.Single(metrics).Total);
    }

    [Fact]
    public async Task Machine_without_active_context_is_skipped()
    {
        var first = DateTimeOffset.UtcNow;
        var second = first.AddSeconds(5);
        var repository = new FakeOeeRawIntervalRepository();
        repository.RawIntervals.Add(Raw("M16-01", first, shotOkTotal: 100));
        var builder = CreateBuilder(repository);

        var metrics = await builder.BuildMetricsAsync("GW-M16-01", [Raw("M16-01", second, shotOkTotal: 110)], new Dictionary<string, ProductionContext>(), second.ToUnixTimeSeconds(), CancellationToken.None);

        Assert.Empty(metrics);
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
        long? errorTimeTotal = null) =>
        new()
        {
            MachineCode = machineCode,
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

        public Task<PlcRawInterval?> GetPreviousAsync(string machineCode, long beforeReadAtUnixTimeSeconds, CancellationToken cancellationToken) =>
            Task.FromResult(RawIntervals
                .Where(raw => raw.MachineCode.Equals(machineCode, StringComparison.OrdinalIgnoreCase) && raw.ReadAtUnixTimeSeconds < beforeReadAtUnixTimeSeconds)
                .OrderByDescending(raw => raw.ReadAtUnixTimeSeconds)
                .FirstOrDefault());
    }
}
