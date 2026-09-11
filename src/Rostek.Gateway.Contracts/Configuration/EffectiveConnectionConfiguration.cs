namespace Rostek.Gateway.Contracts.Configuration;

public static class OeeTimeSources
{
    public const string OptionName = "oee_time_source";
    public const string DeviceCounters = "device_counters";
    public const string GatewayState = "gateway_state";
    public const string Auto = "auto";

    public static bool IsValid(string? value) =>
        value is not null &&
        (value.Equals(DeviceCounters, StringComparison.OrdinalIgnoreCase) ||
         value.Equals(GatewayState, StringComparison.OrdinalIgnoreCase) ||
         value.Equals(Auto, StringComparison.OrdinalIgnoreCase));

    public static string FromOptions(IReadOnlyDictionary<string, object>? options)
    {
        if (options is null || !options.TryGetValue(OptionName, out var value))
        {
            return DeviceCounters;
        }

        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)?.Trim().ToLowerInvariant()
            ?? DeviceCounters;
    }
}

public sealed record EffectiveConnectionConfiguration(
    string Protocol,
    string? Host,
    int? Port,
    string? EndpointUrl,
    int? UnitId,
    string? SecurityMode,
    string? SecurityPolicy,
    string? AuthenticationMode,
    string? CredentialReference,
    int ConnectTimeoutMs,
    int RequestTimeoutMs,
    int RetryCount,
    int? PollingIntervalMs,
    IReadOnlyDictionary<string, object>? Options);
