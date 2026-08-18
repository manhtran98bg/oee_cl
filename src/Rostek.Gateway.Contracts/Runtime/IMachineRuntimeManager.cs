using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.Machines;

namespace Rostek.Gateway.Contracts.Runtime;

public interface IMachineRuntimeManager
{
    Task StartAsync(CancellationToken cancellationToken);
    Task ApplyConfigurationAsync(RuntimeConfiguration previous, RuntimeConfiguration current, CancellationToken cancellationToken);
    IReadOnlyCollection<MachineRuntimeStatusDto> GetStatuses();
}
