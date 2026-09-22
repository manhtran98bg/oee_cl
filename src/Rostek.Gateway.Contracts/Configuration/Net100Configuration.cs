namespace Rostek.Gateway.Contracts.Configuration;

public static class Net100Configuration
{
    public const string Protocol = "NET100_HTTP";
    public const string DefaultBasePath = "/net100";

    public const string MachineStateSource = "live.machine_state";
    public const string StatusSource = "live.status";
    public const string AlarmSource = "live.alarm";
    public const string ShotNumberSource = "live.shot_no";
    public const string CycleTimeMsSource = "lastshotinfo.cycle_time_ms";
    public const string QualityCodeSource = "lastshotinfo.quality_code";

    public static bool IsProtocol(string? protocol) =>
        protocol is not null &&
        (protocol.Equals(Protocol, StringComparison.OrdinalIgnoreCase) ||
         protocol.Equals("NET100HTTP", StringComparison.OrdinalIgnoreCase));

    public static string NormalizeBasePath(string? basePath)
    {
        var value = string.IsNullOrWhiteSpace(basePath) ? DefaultBasePath : basePath.Trim();
        return "/" + value.Trim('/');
    }

    public static string BuildBaseUrl(string host, int port, string? basePath) =>
        $"http://{host.Trim()}:{port}{NormalizeBasePath(basePath)}";
}
