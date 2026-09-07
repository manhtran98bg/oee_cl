namespace Rostek.Gateway.Domain.Entities;

public sealed class ProductionPeriod
{
    public string PeriodId { get; set; } = string.Empty;
    public string Machine { get; set; } = string.Empty;
    public int PlcPeriodIndex { get; set; }
    public string Mode { get; set; } = "production";
    public string OrderId { get; set; } = string.Empty;
    public string ServerOrderId { get; set; } = string.Empty;
    public string ProductsJson { get; set; } = "[]";
    public string TagsJson { get; set; } = "[]";
    public string ExtraJson { get; set; } = "{}";
    public long StartAt { get; set; }
    public long EndAt { get; set; }
    public string Status { get; set; } = "active";
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public long UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
