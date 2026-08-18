using Rostek.Gateway.Contracts.Machines;

namespace Rostek.Gateway.Contracts.Runtime;

public interface IMachineValueReader
{
    IReadOnlyCollection<MachineValueSnapshotDto> GetSnapshots();
    MachineValueSnapshotDto? GetSnapshot(string machineCode);
}
