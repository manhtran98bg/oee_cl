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

public interface IMachineRuntimeFactory
{
    IMachineRuntime Create(EffectiveMachineConfiguration configuration);
}

public interface IMachineRuntimeManager
{
    Task StartAsync(CancellationToken cancellationToken);
    Task ApplyConfigurationAsync(RuntimeConfiguration previous, RuntimeConfiguration current, CancellationToken cancellationToken);
    IReadOnlyCollection<MachineRuntimeStatusDto> GetStatuses();
}

public interface IRuntimeConfigurationProvider
{
    RuntimeConfiguration Current { get; }
    Task ReplaceAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken);
}

public interface IRuntimeStatusReader
{
    IReadOnlyCollection<MachineRuntimeStatusDto> GetStatuses();
    MachineRuntimeStatusDto? GetStatus(string machineCode);
}

public interface IMachineValueReader
{
    IReadOnlyCollection<MachineValueSnapshotDto> GetSnapshots();
    MachineValueSnapshotDto? GetSnapshot(string machineCode);
}
