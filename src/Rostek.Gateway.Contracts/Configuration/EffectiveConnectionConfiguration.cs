namespace Rostek.Gateway.Contracts.Configuration;

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
