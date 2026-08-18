namespace Rostek.Gateway.Application.MachineGroups;

public sealed class MachineGroupInput
{
    public Guid? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed record MachineGroupListItem(Guid Id, string Code, string Name, string? Description, int DisplayOrder);
