namespace Rostek.Gateway.Domain.Entities;

public sealed class PlcRawInterval
{
    public long Id { get; set; }
    public string MachineCode { get; set; } = string.Empty;
    public long ReadAtUnixTimeSeconds { get; set; }
    public int? MachineState { get; set; }
    public long? ShotOkTotal { get; set; }
    public long? ShotNgTotal { get; set; }
    public int? CycleTimeMs { get; set; }
    public long? RunTimeTotal { get; set; }
    public long? StopTimeTotal { get; set; }
    public long? ErrorTimeTotal { get; set; }
    public long CreatedUnixTimeSeconds { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
