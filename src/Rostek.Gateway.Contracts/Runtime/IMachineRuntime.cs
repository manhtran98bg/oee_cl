using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.Machines;

namespace Rostek.Gateway.Contracts.Runtime;

public interface IMachineRuntime : IAsyncDisposable
{
    string MachineCode { get; }
    MachineRuntimeStatusDto Status { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task ApplyConfigurationAsync(EffectiveMachineConfiguration configuration, CancellationToken cancellationToken);
}
