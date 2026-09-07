namespace Rostek.Gateway.Domain.Entities;

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
