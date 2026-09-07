using Rostek.Gateway.Application.Common;

namespace Rostek.Gateway.Application.MachineGroups;

public interface IMachineGroupService
{
    Task<IReadOnlyList<MachineGroupListItem>> ListAsync(CancellationToken cancellationToken);
    Task<MachineGroupInput?> GetInputAsync(Guid id, CancellationToken cancellationToken);
    Task<GatewayResult<Guid>> SaveAsync(MachineGroupInput input, string? userName, CancellationToken cancellationToken);
}

public sealed class MachineGroupInput
{
    public Guid? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed record MachineGroupListItem(Guid Id, string Code, string Name, string? Description, int DisplayOrder);
