using Rostek.Gateway.Domain.Enums;

namespace Rostek.Gateway.Domain.Entities;

public sealed class ConfigurationVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Version { get; set; }
    public ConfigurationVersionStatus Status { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
    public string Checksum { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AppliedAtUtc { get; set; }
}
