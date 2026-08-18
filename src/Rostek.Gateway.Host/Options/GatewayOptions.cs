namespace Rostek.Gateway.Host.Options;

public sealed class GatewayOptions
{
    public string GatewayId { get; set; } = "GW-M16-01";
    public string DataDirectory { get; set; } = "data";
    public string BackupDirectory { get; set; } = "backups";
    public string ExportDirectory { get; set; } = "exports";
}
