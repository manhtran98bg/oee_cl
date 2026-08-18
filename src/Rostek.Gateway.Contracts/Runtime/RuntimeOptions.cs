namespace Rostek.Gateway.Contracts.Runtime;

public sealed class RuntimeOptions
{
    public int MaxInitialConcurrentConnections { get; set; } = 5;
    public int MaxReconnectConcurrentConnections { get; set; } = 5;
    public int MinimumPollingIntervalMs { get; set; } = 200;
    public int MaxReconnectBackoffMs { get; set; } = 30000;
    public int ShutdownTimeoutSeconds { get; set; } = 30;
}
