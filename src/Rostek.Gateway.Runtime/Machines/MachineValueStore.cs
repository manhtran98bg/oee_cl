using System.Collections.Concurrent;
using Rostek.Gateway.Contracts.Machines;
using Rostek.Gateway.Contracts.Runtime;

namespace Rostek.Gateway.Runtime.Machines;

public sealed class MachineValueStore : IMachineValueReader
{
    private readonly ConcurrentDictionary<string, MachineValueSnapshotDto> _snapshots = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<MachineValueSnapshotDto> GetSnapshots() =>
        _snapshots.Values.OrderBy(snapshot => snapshot.MachineCode).ToList();

    public MachineValueSnapshotDto? GetSnapshot(string machineCode) =>
        _snapshots.TryGetValue(machineCode, out var snapshot) ? snapshot : null;

    public void ReplaceSnapshot(string machineCode, bool online, DateTimeOffset? lastReadUtc, IReadOnlyList<SignalValueDto> values)
    {
        _snapshots[machineCode] = new MachineValueSnapshotDto(machineCode, online, lastReadUtc, values);
    }

    public void MarkOffline(string machineCode, string? error)
    {
        var now = DateTimeOffset.UtcNow;
        _snapshots.AddOrUpdate(
            machineCode,
            _ => new MachineValueSnapshotDto(machineCode, false, null, []),
            (_, previous) =>
            {
                var values = previous.Values
                    .Select(value => value with
                    {
                        Quality = MachineReadQuality.Disconnected,
                        TimestampUtc = now,
                        Error = error
                    })
                    .ToList();

                return previous with { Online = false, Values = values };
            });
    }

    public void Remove(string machineCode)
    {
        _snapshots.TryRemove(machineCode, out _);
    }
}
