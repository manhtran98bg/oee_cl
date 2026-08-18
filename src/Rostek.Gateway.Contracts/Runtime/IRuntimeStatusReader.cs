using Rostek.Gateway.Contracts.Machines;

namespace Rostek.Gateway.Contracts.Runtime;

public interface IRuntimeStatusReader
{
    IReadOnlyCollection<MachineRuntimeStatusDto> GetStatuses();
    MachineRuntimeStatusDto? GetStatus(string machineCode);
}
