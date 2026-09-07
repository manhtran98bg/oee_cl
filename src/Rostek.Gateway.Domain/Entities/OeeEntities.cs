namespace Rostek.Gateway.Domain.Entities;

public sealed class ProductionContext
{
    public string Machine { get; set; } = string.Empty;
    public string Mode { get; set; } = "production";
    public string OrderId { get; set; } = string.Empty;
    public string ServerOrderId { get; set; } = string.Empty;
    public string ActivePeriodId { get; set; } = string.Empty;
    public int CurrentPlcPeriodIndex { get; set; }
    public string ProductsJson { get; set; } = "[]";
    public string TagsJson { get; set; } = "[]";
    public string ExtraJson { get; set; } = "{}";
    public string Status { get; set; } = "active";
    public long UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}

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

public sealed class PlcRawInterval
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Machine { get; set; } = string.Empty;
    public long ReadAt { get; set; }
    public int PlcPeriodIndex { get; set; }
    public string RunState { get; set; } = "disconnect";
    public int PlcBootCounter { get; set; }
    public int StatusFlags { get; set; }
    public int PeriodActive { get; set; }
    public int PlcRestarted { get; set; }
    public long ShotOkTotal { get; set; }
    public long ShotNgTotal { get; set; }
    public long MoldOpenTotal { get; set; }
    public long RunTimeTotalSec { get; set; }
    public long StopTimeTotalSec { get; set; }
    public long ErrorTimeTotalSec { get; set; }
    public int CycleTimeMs { get; set; }
    public int CycleAvg10TimeMs { get; set; }
}

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

public sealed class ProductMetric
{
    public string Machine { get; set; } = string.Empty;
    public string ServerOrderId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public long StartAt { get; set; }
    public long EndAt { get; set; }
    public int TotalQty { get; set; }
    public int NgQty { get; set; }
    public int CountCheckQty { get; set; }
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
    public int TargetQty { get; set; }
    public string Status { get; set; } = "process";
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public long UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}

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
