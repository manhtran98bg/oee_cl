using Rostek.Gateway.Application.Common;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Contracts.Configuration;

namespace Rostek.Gateway.Application.Machines;

public interface IMachineService
{
    Task<MachineListResult> ListAsync(MachineQuery query, CancellationToken cancellationToken);
    Task<MachineEditInput?> GetInputAsync(Guid id, CancellationToken cancellationToken);
    Task<GatewayResult<Guid>> SaveAsync(MachineEditInput input, string? userName, CancellationToken cancellationToken);
    Task<GatewayResult<Guid>> CloneAsync(Guid id, string newCode, string? userName, CancellationToken cancellationToken);
    Task<GatewayResult> SetEnabledAsync(Guid id, bool enabled, string? userName, CancellationToken cancellationToken);
    Task<RuntimeConfiguration?> PreviewEffectiveAsync(Guid machineId, CancellationToken cancellationToken);
}

public interface ISignalOverrideService
{
    Task<GatewayResult> SaveAsync(SignalOverrideInput input, string? userName, CancellationToken cancellationToken);
    Task<GatewayResult> ResetAsync(Guid machineId, Guid templateSignalId, string? userName, CancellationToken cancellationToken);
}
