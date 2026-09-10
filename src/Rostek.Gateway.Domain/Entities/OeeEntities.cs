namespace Rostek.Gateway.Domain.Entities;

public sealed class ProductionContext
{
    public string SessionId { get; set; } = string.Empty;
    public string Machine { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public string OrderId { get; set; } = string.Empty;
    public string ServerOrderId { get; set; } = string.Empty;
    public long ActivePeriodStartAt { get; set; }
    public int CurrentPlcPeriodIndex { get; set; }
    public string ProductsJson { get; set; } = "[]";
    public string ExtraJson { get; set; } = "{}";
    public string BaselineRawId { get; set; } = string.Empty;
    public long BaselineCapturedAt { get; set; }
    public long BaselineShotOkTotal { get; set; }
    public long BaselineShotNgTotal { get; set; }
    public long BaselineRunTimeTotalSec { get; set; }
    public long BaselineStopTimeTotalSec { get; set; }
    public long BaselineErrorTimeTotalSec { get; set; }
    public int BaselineCycleTimeMs { get; set; }
    public long UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}

public sealed class ProductionPeriod
{
    public string PeriodId { get; set; } = string.Empty;
    public string Machine { get; set; } = string.Empty;
    public int PlcPeriodIndex { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string ServerOrderId { get; set; } = string.Empty;
    public string ProductsJson { get; set; } = "[]";
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
    public int PeriodActive { get; set; }
    public long ShotOkTotal { get; set; }
    public long ShotNgTotal { get; set; }
    public long RunTimeTotalSec { get; set; }
    public long StopTimeTotalSec { get; set; }
    public long ErrorTimeTotalSec { get; set; }
    public int CycleTimeMs { get; set; }
}
