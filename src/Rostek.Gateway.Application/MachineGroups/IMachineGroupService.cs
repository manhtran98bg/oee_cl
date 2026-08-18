using Rostek.Gateway.Application.Common;

namespace Rostek.Gateway.Application.MachineGroups;

public interface IMachineGroupService
{
    Task<IReadOnlyList<MachineGroupListItem>> ListAsync(CancellationToken cancellationToken);
    Task<MachineGroupInput?> GetInputAsync(Guid id, CancellationToken cancellationToken);
    Task<GatewayResult<Guid>> SaveAsync(MachineGroupInput input, string? userName, CancellationToken cancellationToken);
}
