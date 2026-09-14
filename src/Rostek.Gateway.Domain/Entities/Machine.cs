namespace Rostek.Gateway.Domain.Entities;

public sealed class Machine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? Serial { get; set; }
    public string? Manufacturer { get; set; }
    public string? Location { get; set; }
    public Guid? GroupId { get; set; }
    public MachineGroup? Group { get; set; }
    public Guid TemplateId { get; set; }
    public MachineTemplate? Template { get; set; }
    public bool Enabled { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public MachineConnection? Connection { get; set; }
    public List<MachineSignalOverride> SignalOverrides { get; set; } = [];
}
