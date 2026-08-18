namespace Rostek.Gateway.Application.History;

public sealed class HistoryOptions
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "Postgres";
    public int SampleIntervalMs { get; set; } = 5000;
    public int BatchSize { get; set; } = 500;
    public int CommandTimeoutSeconds { get; set; } = 10;
    public string ConnectionString { get; set; } = string.Empty;
    public int RetentionDays { get; set; } = 90;
}
