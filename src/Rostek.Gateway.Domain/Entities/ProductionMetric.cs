namespace Rostek.Gateway.Domain.Entities;

public sealed class ProductionMetric
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string MetricType { get; set; } = string.Empty;
    public string Machine { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string ServerOrderId { get; set; } = string.Empty;
    public string PeriodId { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string RunState { get; set; } = string.Empty;
    public long StartAt { get; set; }
    public long EndAt { get; set; }
    public int TotalQty { get; set; }
    public int NgQty { get; set; }
    public decimal PlanQty { get; set; }
    public int ProdTimeSec { get; set; }
    public int RunTimeSec { get; set; }
    public int StopTimeSec { get; set; }
    public int ErrorTimeSec { get; set; }
    public decimal Availability { get; set; }
    public decimal Performance { get; set; }
    public decimal Quality { get; set; }
    public decimal ActualCycleSec { get; set; }
    public decimal Oee { get; set; }
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public long UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
