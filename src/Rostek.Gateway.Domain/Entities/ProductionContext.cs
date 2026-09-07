using Rostek.Gateway.Domain.Enums;

namespace Rostek.Gateway.Domain.Entities;

public sealed class ProductionContext
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string MachineCode { get; set; } = string.Empty;
    public string CommandCode { get; set; } = string.Empty;
    public ProductionContextStatus Status { get; set; } = ProductionContextStatus.Stopped;
    public string? ProductionOrderCode { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string? OperatorCode { get; set; }
    public string? ReasonCode { get; set; }
    public string? Note { get; set; }
    public long? StartedUnixTimeSeconds { get; set; }
    public long? PausedUnixTimeSeconds { get; set; }
    public long? StoppedUnixTimeSeconds { get; set; }
    public long UpdatedUnixTimeSeconds { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
