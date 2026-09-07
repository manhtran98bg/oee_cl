using Microsoft.Extensions.Logging.Abstractions;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Contracts.Machines;
using Rostek.Gateway.Contracts.Runtime;
using Rostek.Gateway.Domain.Entities;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class OeeRawIntervalServiceTests
{
    [Fact]
    public async Task Capture_maps_latest_snapshot_to_aligned_raw_interval()
    {
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(10);
        var reader = new MutableMachineValueReader();
        var repository = new FakeOeeRawIntervalRepository();
        var service = CreateService(reader, repository);
        reader.SetSnapshots([CreateSnapshot("M16-01", timestamp, machineState: 1, shotOkCount: 100, shotNgCount: 2, cycleTimeMs: 1500, runTimeTotal: 10000, stopTimeTotal: 2000, errorTimeTotal: 500)]);

        var inserted = await service.CaptureAsync(TimeSpan.FromSeconds(5), TestContexts("M16-01"), true, CancellationToken.None);

        var raw = Assert.Single(inserted);
        Assert.Equal("M16-01", raw.MachineCode);
        Assert.Equal("MO-001", raw.ProductionOrderCode);
        Assert.Equal("SESSION-001", raw.SessionId);
        Assert.Equal(10, raw.ReadAtUnixTimeSeconds);
        Assert.Equal(1, raw.MachineState);
        Assert.Equal(100, raw.ShotOkTotal);
        Assert.Equal(2, raw.ShotNgTotal);
        Assert.Equal(1500, raw.CycleTimeMs);
        Assert.Equal(10000, raw.RunTimeTotal);
        Assert.Equal(2000, raw.StopTimeTotal);
        Assert.Equal(500, raw.ErrorTimeTotal);
    }

    [Fact]
    public async Task Capture_skips_offline_snapshot()
    {
        var reader = new MutableMachineValueReader();
        var repository = new FakeOeeRawIntervalRepository();
        var service = CreateService(reader, repository);
        reader.SetSnapshots([CreateSnapshot("M16-01", DateTimeOffset.UtcNow, online: false, shotOkCount: 100)]);

        var inserted = await service.CaptureAsync(TimeSpan.FromSeconds(5), TestContexts("M16-01"), true, CancellationToken.None);

        Assert.Empty(inserted);
    }

    [Fact]
    public async Task Capture_does_not_insert_duplicate_machine_interval()
    {
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(10);
        var reader = new MutableMachineValueReader();
        var repository = new FakeOeeRawIntervalRepository();
        var service = CreateService(reader, repository);
        reader.SetSnapshots([CreateSnapshot("M16-01", timestamp, shotOkCount: 100)]);
        await service.CaptureAsync(TimeSpan.FromSeconds(5), TestContexts("M16-01"), true, CancellationToken.None);

        var second = await service.CaptureAsync(TimeSpan.FromSeconds(5), TestContexts("M16-01"), true, CancellationToken.None);

        Assert.Empty(second);
        Assert.Single(repository.RawIntervals);
    }

    private static OeeRawIntervalService CreateService(MutableMachineValueReader reader, FakeOeeRawIntervalRepository repository) =>
        new(reader, repository, NullLogger<OeeRawIntervalService>.Instance);

    private static IReadOnlyDictionary<string, ProductionContext> TestContexts(string machineCode) =>
        new Dictionary<string, ProductionContext>(StringComparer.OrdinalIgnoreCase)
        {
            [machineCode] = new()
            {
                MachineCode = machineCode,
                CommandCode = "CMD-001",
                ProductionOrderCode = "MO-001",
                SessionId = "SESSION-001",
                Status = Domain.Enums.ProductionContextStatus.Started
            }
        };

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

    private sealed class FakeOeeRawIntervalRepository : IOeeRawIntervalRepository
    {
        public List<PlcRawInterval> RawIntervals { get; } = [];

        public Task<IReadOnlyList<PlcRawInterval>> InsertMissingAsync(IReadOnlyCollection<PlcRawInterval> rawIntervals, CancellationToken cancellationToken)
        {
            var inserted = rawIntervals
                .Where(raw => !RawIntervals.Any(existing =>
                    existing.MachineCode.Equals(raw.MachineCode, StringComparison.OrdinalIgnoreCase) &&
                    existing.ProductionOrderCode.Equals(raw.ProductionOrderCode, StringComparison.OrdinalIgnoreCase) &&
                    existing.SessionId.Equals(raw.SessionId, StringComparison.OrdinalIgnoreCase) &&
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
            Task.FromResult<PlcRawInterval?>(null);
    }
}
