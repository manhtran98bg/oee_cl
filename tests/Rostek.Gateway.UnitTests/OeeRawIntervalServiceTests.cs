using Microsoft.Extensions.Logging.Abstractions;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Contracts.Machines;
using Rostek.Gateway.Contracts.Runtime;
using Rostek.Gateway.UnitTests.Fakes;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class OeeRawIntervalServiceTests
{
    [Fact]
    public async Task Capture_maps_latest_snapshot_to_python_raw_interval()
    {
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(12);
        var reader = new MutableMachineValueReader();
        var repository = new FakeOeeLocalRepository();
        var service = CreateService(reader, repository);
        reader.SetSnapshots([CreateSnapshot("M16-01", timestamp, machineState: 1, shotOkCount: 100, shotNgCount: 2, cycleTimeMs: 1500, runTimeTotal: 10, stopTimeTotal: 2, errorTimeTotal: 1)]);

        var inserted = await service.CaptureAsync(TimeSpan.FromSeconds(5), requireProductionContext: false, CancellationToken.None);

        var raw = Assert.Single(inserted);
        Assert.Equal("M16-01", raw.Machine);
        Assert.Equal(10, raw.ReadAt);
        Assert.Equal(1, raw.PlcPeriodIndex);
        Assert.Equal("run", raw.RunState);
        Assert.Equal(100, raw.ShotOkTotal);
        Assert.Equal(2, raw.ShotNgTotal);
        Assert.Equal(1500, raw.CycleTimeMs);
        Assert.Equal(10, raw.RunTimeTotalSec);
        Assert.Equal(2, raw.StopTimeTotalSec);
        Assert.Equal(1, raw.ErrorTimeTotalSec);
        Assert.Single(repository.Contexts);
        Assert.Single(repository.Periods);
    }

    [Fact]
    public async Task Capture_skips_without_context_when_required()
    {
        var reader = new MutableMachineValueReader();
        var repository = new FakeOeeLocalRepository();
        var service = CreateService(reader, repository);
        reader.SetSnapshots([CreateSnapshot("M16-01", DateTimeOffset.UtcNow, shotOkCount: 100)]);

        var inserted = await service.CaptureAsync(TimeSpan.FromSeconds(5), requireProductionContext: true, CancellationToken.None);

        Assert.Empty(inserted);
        Assert.Empty(repository.Contexts);
    }

    [Fact]
    public async Task Capture_uses_production_context_loaded_in_memory_when_required()
    {
        var reader = new MutableMachineValueReader();
        var repository = new FakeOeeLocalRepository();
        var store = new ProductionContextStore();
        await repository.EnsureTestProductionContextAsync("M16-01", 10, CancellationToken.None);
        store.Replace(await repository.ListProductionContextsAsync(CancellationToken.None));
        var service = CreateService(reader, repository, store);
        reader.SetSnapshots([CreateSnapshot("M16-01", DateTimeOffset.FromUnixTimeSeconds(10), shotOkCount: 100)]);

        var inserted = await service.CaptureAsync(TimeSpan.FromSeconds(5), requireProductionContext: true, CancellationToken.None);

        Assert.Single(inserted);
    }

    [Fact]
    public async Task Capture_does_not_insert_duplicate_machine_interval()
    {
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(10);
        var reader = new MutableMachineValueReader();
        var repository = new FakeOeeLocalRepository();
        var service = CreateService(reader, repository);
        reader.SetSnapshots([CreateSnapshot("M16-01", timestamp, shotOkCount: 100)]);
        await service.CaptureAsync(TimeSpan.FromSeconds(5), false, CancellationToken.None);

        var second = await service.CaptureAsync(TimeSpan.FromSeconds(5), false, CancellationToken.None);

        Assert.Empty(second);
        Assert.Single(repository.RawIntervals);
    }

    private static OeeRawIntervalService CreateService(
        MutableMachineValueReader reader,
        FakeOeeLocalRepository repository,
        ProductionContextStore? store = null) =>
        new(reader, repository, store ?? new ProductionContextStore(), NullLogger<OeeRawIntervalService>.Instance);

    private static MachineValueSnapshotDto CreateSnapshot(
        string machineCode,
        DateTimeOffset timestamp,
        bool online = true,
        int? machineState = null,
        long? shotOkCount = null,
        long? shotNgCount = null,
        int? cycleTimeMs = null,
        long? runTimeTotal = null,
        long? stopTimeTotal = null,
        long? errorTimeTotal = null)
    {
        var values = new List<SignalValueDto>();
        Add(values, OeeSignalCodes.MachineState, machineState, timestamp);
        Add(values, OeeSignalCodes.ShotOkCount, shotOkCount, timestamp);
        Add(values, OeeSignalCodes.ShotNgCount, shotNgCount, timestamp);
        Add(values, OeeSignalCodes.CycleTimeMs, cycleTimeMs, timestamp);
        Add(values, OeeSignalCodes.RunTimeTotal, runTimeTotal, timestamp);
        Add(values, OeeSignalCodes.StopTimeTotal, stopTimeTotal, timestamp);
        Add(values, OeeSignalCodes.ErrorTimeTotal, errorTimeTotal, timestamp);
        return new MachineValueSnapshotDto(machineCode, online, timestamp, values);
    }

    private static void Add(List<SignalValueDto> values, string code, object? value, DateTimeOffset timestamp)
    {
        if (value is not null)
        {
            values.Add(new SignalValueDto(code, value, "Int64", MachineReadQuality.Good, timestamp, null));
        }
    }

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
}
