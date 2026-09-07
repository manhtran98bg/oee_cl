namespace Rostek.Gateway.Domain.Entities;

public sealed class DowntimeEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Machine { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string ServerOrderId { get; set; } = string.Empty;
    public string PeriodId { get; set; } = string.Empty;
    public int PlcPeriodIndex { get; set; }
    public string State { get; set; } = string.Empty;
    public long StartAt { get; set; }
    public long EndAt { get; set; }
    public int DurationSec { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string OrderExtraJson { get; set; } = "{}";
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public long UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
