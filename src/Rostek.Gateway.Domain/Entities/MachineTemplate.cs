using Rostek.Gateway.Domain.Enums;

namespace Rostek.Gateway.Domain.Entities;

public sealed class MachineTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public GatewayProtocol Protocol { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public int DefaultPollingIntervalMs { get; set; } = 1000;
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<TemplateSignal> Signals { get; set; } = [];
}
