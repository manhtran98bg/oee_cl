namespace Rostek.Gateway.Domain.Entities;

public sealed class MesSyncOutboxMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Topic { get; set; } = string.Empty;
    public string SourceTable { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "pending";
    public int RetryCount { get; set; }
    public string LastError { get; set; } = string.Empty;
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public long UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public long SyncedAt { get; set; }
}
