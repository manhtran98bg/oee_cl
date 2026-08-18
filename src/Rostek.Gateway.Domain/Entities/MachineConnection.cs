using Rostek.Gateway.Domain.Enums;

namespace Rostek.Gateway.Domain.Entities;

public sealed class MachineConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MachineId { get; set; }
    public Machine? Machine { get; set; }
    public GatewayProtocol Protocol { get; set; }
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? EndpointUrl { get; set; }
    public int? UnitId { get; set; }
    public string? SecurityMode { get; set; }
    public string? SecurityPolicy { get; set; }
    public string? AuthenticationMode { get; set; }
    public string? CredentialReference { get; set; }
    public int ConnectTimeoutMs { get; set; } = 3000;
    public int RequestTimeoutMs { get; set; } = 3000;
    public int RetryCount { get; set; } = 3;
    public int? PollingIntervalMs { get; set; }
    public string? OptionsJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
