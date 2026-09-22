using Microsoft.Extensions.Logging.Abstractions;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.Machines;
using Rostek.Gateway.Contracts.Runtime;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.UnitTests.Support;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class RawDataCaptureServiceTests
{
    [Fact]
    public async Task Capture_maps_latest_snapshot_to_python_raw_interval()
    {
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(12);
        var reader = new MutableMachineValueReader();
        var repository = new InMemoryOeeLocalRepository();
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
    public async Task Capture_stores_inactive_raw_without_context_when_required()
    {
        var reader = new MutableMachineValueReader();
        var repository = new InMemoryOeeLocalRepository();
        var service = CreateService(reader, repository);
        reader.SetSnapshots([CreateSnapshot("M16-01", DateTimeOffset.UtcNow, shotOkCount: 100)]);

        var inserted = await service.CaptureAsync(TimeSpan.FromSeconds(5), requireProductionContext: true, CancellationToken.None);

        var raw = Assert.Single(inserted);
        Assert.Equal(0, raw.PlcPeriodIndex);
        Assert.Equal(0, raw.PeriodActive);
        Assert.Empty(repository.Contexts);
        Assert.Empty(repository.Periods);
    }

    [Fact]
    public async Task Capture_uses_production_context_loaded_in_memory_when_required()
    {
        var reader = new MutableMachineValueReader();
        var repository = new InMemoryOeeLocalRepository();
        var cache = new ProductionContextCache();
        await repository.EnsureTestProductionContextAsync("M16-01", 10, CancellationToken.None);
        cache.Replace(await repository.ListProductionContextsAsync(CancellationToken.None));
        var service = CreateService(reader, repository, cache);
        reader.SetSnapshots([CreateSnapshot("M16-01", DateTimeOffset.FromUnixTimeSeconds(10), shotOkCount: 100)]);

        var inserted = await service.CaptureAsync(TimeSpan.FromSeconds(5), requireProductionContext: true, CancellationToken.None);

        Assert.Single(inserted);
    }

    [Fact]
    public async Task Capture_does_not_insert_duplicate_machine_interval()
    {
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(10);
        var reader = new MutableMachineValueReader();
        var repository = new InMemoryOeeLocalRepository();
        var service = CreateService(reader, repository);
        reader.SetSnapshots([CreateSnapshot("M16-01", timestamp, shotOkCount: 100)]);
        await service.CaptureAsync(TimeSpan.FromSeconds(5), false, CancellationToken.None);

        var second = await service.CaptureAsync(TimeSpan.FromSeconds(5), false, CancellationToken.None);

        Assert.Empty(second);
        Assert.Single(repository.RawIntervals);
    }

    [Theory]
    [InlineData(0, OeeRunStates.Stop)]
    [InlineData(1, OeeRunStates.Run)]
    [InlineData(2, OeeRunStates.Stop)]
    [InlineData(3, OeeRunStates.Error)]
    public async Task Capture_maps_numeric_machine_state(int machineState, string expectedRunState)
    {
        var reader = new MutableMachineValueReader();
        var repository = new InMemoryOeeLocalRepository();
        var service = CreateService(reader, repository);
        reader.SetSnapshots([CreateSnapshot("M16-01", DateTimeOffset.FromUnixTimeSeconds(10), machineState: machineState)]);

        var inserted = await service.CaptureAsync(TimeSpan.FromSeconds(5), false, CancellationToken.None);

        Assert.Equal(expectedRunState, Assert.Single(inserted).RunState);
    }

    [Fact]
    public async Task Capture_gateway_state_counts_elapsed_seconds_for_current_state()
    {
        var reader = new MutableMachineValueReader();
        var repository = new InMemoryOeeLocalRepository();
        var service = CreateService(reader, repository, timeSource: OeeTimeSources.GatewayState);

        reader.SetSnapshots([CreateSnapshot("M16-01", DateTimeOffset.FromUnixTimeSeconds(10), machineState: 1)]);
        var first = await service.CaptureAsync(TimeSpan.FromSeconds(5), false, CancellationToken.None);
        reader.SetSnapshots([CreateSnapshot("M16-01", DateTimeOffset.FromUnixTimeSeconds(15), machineState: 1)]);
        var second = await service.CaptureAsync(TimeSpan.FromSeconds(5), false, CancellationToken.None);
        reader.SetSnapshots([CreateSnapshot("M16-01", DateTimeOffset.FromUnixTimeSeconds(20), machineState: 2)]);
        var third = await service.CaptureAsync(TimeSpan.FromSeconds(5), false, CancellationToken.None);

        Assert.Equal(0, Assert.Single(first).RunTimeTotalSec);
        Assert.Equal(5, Assert.Single(second).RunTimeTotalSec);
        Assert.Equal(5, Assert.Single(third).RunTimeTotalSec);
        Assert.Equal(5, Assert.Single(third).StopTimeTotalSec);
    }

    [Fact]
    public async Task Capture_gateway_state_seeds_from_database_without_counting_offline_gap()
    {
        var reader = new MutableMachineValueReader();
        var repository = new InMemoryOeeLocalRepository();
        await repository.InsertMissingRawIntervalsAsync([
            new PlcRawInterval
            {
                Machine = "M16-01",
                ReadAt = 10,
                RunState = OeeRunStates.Run,
                RunTimeTotalSec = 100,
                StopTimeTotalSec = 20,
                ErrorTimeTotalSec = 5
            }
        ], CancellationToken.None);
        var service = CreateService(reader, repository, timeSource: OeeTimeSources.GatewayState);
        reader.SetSnapshots([CreateSnapshot("M16-01", DateTimeOffset.FromUnixTimeSeconds(100), machineState: 1)]);

        var inserted = await service.CaptureAsync(TimeSpan.FromSeconds(5), false, CancellationToken.None);

        var raw = Assert.Single(inserted);
        Assert.Equal(100, raw.RunTimeTotalSec);
        Assert.Equal(20, raw.StopTimeTotalSec);
        Assert.Equal(5, raw.ErrorTimeTotalSec);
    }

    [Fact]
    public async Task Capture_gateway_state_caps_large_gap_to_three_intervals()
    {
        var reader = new MutableMachineValueReader();
        var repository = new InMemoryOeeLocalRepository();
        var service = CreateService(reader, repository, timeSource: OeeTimeSources.GatewayState);
        reader.SetSnapshots([CreateSnapshot("M16-01", DateTimeOffset.FromUnixTimeSeconds(10), machineState: 3)]);
        await service.CaptureAsync(TimeSpan.FromSeconds(5), false, CancellationToken.None);
        reader.SetSnapshots([CreateSnapshot("M16-01", DateTimeOffset.FromUnixTimeSeconds(100), machineState: 3)]);

        var inserted = await service.CaptureAsync(TimeSpan.FromSeconds(5), false, CancellationToken.None);

        Assert.Equal(15, Assert.Single(inserted).ErrorTimeTotalSec);
    }

    [Fact]
    public async Task Capture_auto_uses_device_counters_when_all_timer_signals_exist()
    {
        var reader = new MutableMachineValueReader();
        var repository = new InMemoryOeeLocalRepository();
        var service = CreateService(reader, repository, timeSource: OeeTimeSources.Auto);
        reader.SetSnapshots([CreateSnapshot(
            "M16-01",
            DateTimeOffset.FromUnixTimeSeconds(10),
            machineState: 1,
            runTimeTotal: 21,
            stopTimeTotal: 8,
            errorTimeTotal: 3)]);

        var inserted = await service.CaptureAsync(TimeSpan.FromSeconds(5), false, CancellationToken.None);

        var raw = Assert.Single(inserted);
        Assert.Equal(21, raw.RunTimeTotalSec);
        Assert.Equal(8, raw.StopTimeTotalSec);
        Assert.Equal(3, raw.ErrorTimeTotalSec);
    }

    [Fact]
    public async Task Capture_auto_falls_back_to_gateway_when_a_timer_signal_is_missing()
    {
        var reader = new MutableMachineValueReader();
        var repository = new InMemoryOeeLocalRepository();
        var service = CreateService(reader, repository, timeSource: OeeTimeSources.Auto);
        reader.SetSnapshots([CreateSnapshot("M16-01", DateTimeOffset.FromUnixTimeSeconds(10), machineState: 1, runTimeTotal: 50)]);
        await service.CaptureAsync(TimeSpan.FromSeconds(5), false, CancellationToken.None);
        reader.SetSnapshots([CreateSnapshot("M16-01", DateTimeOffset.FromUnixTimeSeconds(15), machineState: 1, runTimeTotal: 55)]);

        var inserted = await service.CaptureAsync(TimeSpan.FromSeconds(5), false, CancellationToken.None);

        Assert.Equal(5, Assert.Single(inserted).RunTimeTotalSec);
    }

    private static RawDataCaptureService CreateService(
        MutableMachineValueReader reader,
        InMemoryOeeLocalRepository repository,
        ProductionContextCache? cache = null,
        string timeSource = OeeTimeSources.DeviceCounters) =>
        new(
            reader,
            new StaticRuntimeConfigurationProvider(timeSource),
            repository,
            cache ?? new ProductionContextCache(),
            new GatewayOeeTimerService(NullLogger<GatewayOeeTimerService>.Instance),
            NullLogger<RawDataCaptureService>.Instance);

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

    private sealed class StaticRuntimeConfigurationProvider : IRuntimeConfigurationProvider
    {
        public StaticRuntimeConfigurationProvider(string timeSource)
        {
            var connection = new EffectiveConnectionConfiguration(
                "OPCUA",
                null,
                null,
                "opc.tcp://localhost:4840",
                null,
                "NONE",
                "None",
                "ANONYMOUS",
                null,
                3000,
                3000,
                3,
                1000,
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    [OeeTimeSources.OptionName] = timeSource
                });
            var machine = new EffectiveMachineConfiguration(
                Guid.NewGuid(),
                "M16-01",
                "Machine 16",
                "OPCUA",
                true,
                connection,
                []);
            Current = new RuntimeConfiguration(
                1,
                DateTimeOffset.UtcNow,
                new Dictionary<string, EffectiveMachineConfiguration>(StringComparer.OrdinalIgnoreCase)
                {
                    [machine.MachineCode] = machine
                });
        }

        public RuntimeConfiguration Current { get; private set; }

        public Task ReplaceAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken)
        {
            Current = configuration;
            return Task.CompletedTask;
        }
    }
}
