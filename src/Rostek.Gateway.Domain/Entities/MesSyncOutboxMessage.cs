using Rostek.Gateway.Domain.Enums;

namespace Rostek.Gateway.Domain.Entities;

public sealed class MesSyncOutboxMessage
{
    public long Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public MesSyncOutboxStatus Status { get; set; } = MesSyncOutboxStatus.Pending;
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public long NextAttemptUnixTimeSeconds { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public long CreatedUnixTimeSeconds { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public long UpdatedUnixTimeSeconds { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public long? SyncedUnixTimeSeconds { get; set; }
}
