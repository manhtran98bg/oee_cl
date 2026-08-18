using Microsoft.Extensions.Logging.Abstractions;
using Rostek.Gateway.Application.History;
using Rostek.Gateway.Contracts.Machines;
using Rostek.Gateway.Contracts.Runtime;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class DeviceHistorySamplerTests
{
    [Fact]
    public async Task First_sample_only_seeds_baseline()
    {
        var now = DateTimeOffset.UtcNow;
        var reader = new MutableMachineValueReader();
        var writer = new FakeDeviceSampleWriter();
        var sampler = CreateSampler(reader, writer);
        reader.SetSnapshots([CreateSnapshot("M16-01", now, shotOkCount: 100)]);

        var written = await sampler.SampleAndWriteAsync("GW-M16-01", CancellationToken.None);

        Assert.Equal(0, written);
        Assert.Empty(writer.Samples);
    }

    [Fact]
    public async Task Second_sample_writes_delta_from_cumulative_counters()
    {
        var first = DateTimeOffset.UtcNow;
        var second = first.AddSeconds(5);
        var reader = new MutableMachineValueReader();
        var writer = new FakeDeviceSampleWriter();
        var sampler = CreateSampler(reader, writer);
        reader.SetSnapshots([CreateSnapshot("M16-01", first, machineState: 1, shotOkCount: 100, shotNgCount: 10, cycleTimeMs: 1400, runTimeTotalMs: 10000, stopTimeTotalMs: 2000, errorTimeTotalMs: 500)]);
        await sampler.SampleAndWriteAsync("GW-M16-01", CancellationToken.None);
        reader.SetSnapshots([CreateSnapshot("M16-01", second, machineState: 2, shotOkCount: 108, shotNgCount: 11, cycleTimeMs: 1500, runTimeTotalMs: 14000, stopTimeTotalMs: 2500, errorTimeTotalMs: 700)]);

        var written = await sampler.SampleAndWriteAsync("GW-M16-01", CancellationToken.None);

        Assert.Equal(1, written);
        var sample = Assert.Single(writer.Samples);
        Assert.Equal("GW-M16-01", sample.GatewayId);
        Assert.Equal("M16-01", sample.MachineCode);
        Assert.Equal(second, sample.SampledAtUtc);
        Assert.Equal((short)2, sample.MachineState.GetValueOrDefault());
        Assert.Equal(8, sample.ShotOkDelta);
        Assert.Equal(1, sample.ShotNgDelta);
        Assert.Equal(1500, sample.CycleTimeMs);
        Assert.Equal(4000, sample.RunTimeDeltaMs);
        Assert.Equal(500, sample.StopTimeDeltaMs);
        Assert.Equal(200, sample.ErrorTimeDeltaMs);
    }

    [Fact]
    public async Task Cumulative_signal_aliases_keep_existing_delta_codes_working()
    {
        var first = DateTimeOffset.UtcNow;
        var second = first.AddSeconds(5);
        var reader = new MutableMachineValueReader();
        var writer = new FakeDeviceSampleWriter();
        var sampler = CreateSampler(reader, writer);
        reader.SetSnapshots([CreateSnapshotWithCodes("M16-01", first, ("SHOT_OK_DELTA", 10), ("RUN_TIME_DELTA_MS", 1000))]);
        await sampler.SampleAndWriteAsync("GW-M16-01", CancellationToken.None);
        reader.SetSnapshots([CreateSnapshotWithCodes("M16-01", second, ("SHOT_OK_DELTA", 14), ("RUN_TIME_DELTA_MS", 2500))]);

        await sampler.SampleAndWriteAsync("GW-M16-01", CancellationToken.None);

        var sample = Assert.Single(writer.Samples);
        Assert.Equal(4, sample.ShotOkDelta);
        Assert.Equal(1500, sample.RunTimeDeltaMs);
    }

    [Fact]
    public async Task Missing_or_unconvertible_values_produce_null_delta()
    {
        var first = DateTimeOffset.UtcNow;
        var second = first.AddSeconds(5);
        var reader = new MutableMachineValueReader();
        var writer = new FakeDeviceSampleWriter();
        var sampler = CreateSampler(reader, writer);
        reader.SetSnapshots([CreateSnapshotWithCodes("M16-01", first, ("SHOT_OK_COUNT", 100), ("RUN_TIME_TOTAL", "bad"))]);
        await sampler.SampleAndWriteAsync("GW-M16-01", CancellationToken.None);
        reader.SetSnapshots([CreateSnapshotWithCodes("M16-01", second, ("RUN_TIME_TOTAL", 1500))]);

        await sampler.SampleAndWriteAsync("GW-M16-01", CancellationToken.None);

        var sample = Assert.Single(writer.Samples);
        Assert.Null(sample.ShotOkDelta);
        Assert.Null(sample.RunTimeDeltaMs);
    }

    [Fact]
    public async Task Counter_reset_writes_null_delta_and_updates_baseline()
    {
        var first = DateTimeOffset.UtcNow;
        var second = first.AddSeconds(5);
        var third = second.AddSeconds(5);
        var reader = new MutableMachineValueReader();
        var writer = new FakeDeviceSampleWriter();
        var sampler = CreateSampler(reader, writer);
        reader.SetSnapshots([CreateSnapshot("M16-01", first, shotOkCount: 100)]);
        await sampler.SampleAndWriteAsync("GW-M16-01", CancellationToken.None);
        reader.SetSnapshots([CreateSnapshot("M16-01", second, shotOkCount: 90)]);
        await sampler.SampleAndWriteAsync("GW-M16-01", CancellationToken.None);
        reader.SetSnapshots([CreateSnapshot("M16-01", third, shotOkCount: 95)]);

        await sampler.SampleAndWriteAsync("GW-M16-01", CancellationToken.None);

        Assert.Collection(
            writer.Samples,
            sample => Assert.Null(sample.ShotOkDelta),
            sample => Assert.Equal(5, sample.ShotOkDelta));
    }

    [Fact]
    public async Task Machines_keep_independent_baselines()
    {
        var first = DateTimeOffset.UtcNow;
        var second = first.AddSeconds(5);
        var reader = new MutableMachineValueReader();
        var writer = new FakeDeviceSampleWriter();
        var sampler = CreateSampler(reader, writer);
        reader.SetSnapshots([
            CreateSnapshot("M16-01", first, shotOkCount: 10),
            CreateSnapshot("M16-02", first, shotOkCount: 100)
        ]);
        await sampler.SampleAndWriteAsync("GW-M16-01", CancellationToken.None);
        reader.SetSnapshots([
            CreateSnapshot("M16-01", second, shotOkCount: 13),
            CreateSnapshot("M16-02", second, shotOkCount: 107)
        ]);

        await sampler.SampleAndWriteAsync("GW-M16-01", CancellationToken.None);

        Assert.Collection(
            writer.Samples.OrderBy(sample => sample.MachineCode),
            sample =>
            {
                Assert.Equal("M16-01", sample.MachineCode);
                Assert.Equal(3, sample.ShotOkDelta);
            },
            sample =>
            {
                Assert.Equal("M16-02", sample.MachineCode);
                Assert.Equal(7, sample.ShotOkDelta);
            });
    }

    [Fact]
    public async Task Offline_snapshot_is_skipped_and_keeps_previous_baseline()
    {
        var first = DateTimeOffset.UtcNow;
        var offline = first.AddSeconds(5);
        var next = offline.AddSeconds(5);
        var reader = new MutableMachineValueReader();
        var writer = new FakeDeviceSampleWriter();
        var sampler = CreateSampler(reader, writer);
        reader.SetSnapshots([CreateSnapshot("M16-01", first, shotOkCount: 10)]);
        await sampler.SampleAndWriteAsync("GW-M16-01", CancellationToken.None);
        reader.SetSnapshots([CreateSnapshot("M16-01", offline, online: false, shotOkCount: 12)]);
        await sampler.SampleAndWriteAsync("GW-M16-01", CancellationToken.None);
        reader.SetSnapshots([CreateSnapshot("M16-01", next, shotOkCount: 15)]);

        await sampler.SampleAndWriteAsync("GW-M16-01", CancellationToken.None);

        Assert.Equal(5, Assert.Single(writer.Samples).ShotOkDelta);
    }

    [Fact]
    public async Task Online_bad_quality_value_can_still_be_used_as_raw_counter()
    {
        var first = DateTimeOffset.UtcNow;
        var second = first.AddSeconds(5);
        var reader = new MutableMachineValueReader();
        var writer = new FakeDeviceSampleWriter();
        var sampler = CreateSampler(reader, writer);
        reader.SetSnapshots([new MachineValueSnapshotDto("M16-01", true, first, [new SignalValueDto("SHOT_OK_COUNT", 10, "Int32", MachineReadQuality.Bad, first, "Read warning")])]);
        await sampler.SampleAndWriteAsync("GW-M16-01", CancellationToken.None);
        reader.SetSnapshots([new MachineValueSnapshotDto("M16-01", true, second, [new SignalValueDto("SHOT_OK_COUNT", 12, "Int32", MachineReadQuality.Bad, second, "Read warning")])]);

        await sampler.SampleAndWriteAsync("GW-M16-01", CancellationToken.None);

        Assert.Equal(2, Assert.Single(writer.Samples).ShotOkDelta);
    }

    private static DeviceHistorySampler CreateSampler(MutableMachineValueReader reader, FakeDeviceSampleWriter writer) =>
        new(reader, writer, NullLogger<DeviceHistorySampler>.Instance);

    private static MachineValueSnapshotDto CreateSnapshot(
        string machineCode,
        DateTimeOffset timestamp,
        bool online = true,
        int? machineState = null,
        long? shotOkCount = null,
        long? shotNgCount = null,
        int? cycleTimeMs = null,
        long? runTimeTotalMs = null,
        long? stopTimeTotalMs = null,
        long? errorTimeTotalMs = null)
    {
        var values = new List<SignalValueDto>();
        Add(values, "MACHINE_STATE", machineState, timestamp);
        Add(values, "SHOT_OK_COUNT", shotOkCount, timestamp);
        Add(values, "SHOT_NG_COUNT", shotNgCount, timestamp);
        Add(values, "CYCLE_TIME_MS", cycleTimeMs, timestamp);
        Add(values, "RUN_TIME_TOTAL", runTimeTotalMs, timestamp);
        Add(values, "STOP_TIME_TOTAL", stopTimeTotalMs, timestamp);
        Add(values, "ERROR_TIME_TOTAL", errorTimeTotalMs, timestamp);
        return new MachineValueSnapshotDto(machineCode, online, timestamp, values);
    }

    private static MachineValueSnapshotDto CreateSnapshotWithCodes(
        string machineCode,
        DateTimeOffset timestamp,
        params (string Code, object? Value)[] signals) =>
        new(machineCode, true, timestamp, signals.Select(signal => Signal(signal.Code, signal.Value, timestamp)).ToList());

    private static void Add(List<SignalValueDto> values, string code, object? value, DateTimeOffset timestamp)
    {
        if (value is not null)
        {
            values.Add(Signal(code, value, timestamp));
        }
    }

    private static SignalValueDto Signal(string code, object? value, DateTimeOffset timestamp) =>
        new(code, value, "Int64", MachineReadQuality.Good, timestamp, null);

    private sealed class MutableMachineValueReader : IMachineValueReader
    {
        private IReadOnlyCollection<MachineValueSnapshotDto> _snapshots = [];

        public void SetSnapshots(IReadOnlyCollection<MachineValueSnapshotDto> snapshots)
        {
            _snapshots = snapshots;
        }

        public IReadOnlyCollection<MachineValueSnapshotDto> GetSnapshots() => _snapshots;

        public MachineValueSnapshotDto? GetSnapshot(string machineCode) =>
            _snapshots.FirstOrDefault(snapshot => snapshot.MachineCode.Equals(machineCode, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeDeviceSampleWriter : IDeviceSampleWriter
    {
        public List<DeviceSample> Samples { get; } = [];

        public Task EnsureSchemaAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteAsync(IReadOnlyCollection<DeviceSample> samples, CancellationToken cancellationToken)
        {
            Samples.AddRange(samples);
            return Task.CompletedTask;
        }

        public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }
}
