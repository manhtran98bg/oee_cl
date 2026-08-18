namespace Rostek.Gateway.Contracts.Configuration;

public sealed record EffectiveSignalConfiguration(
    string SignalCode,
    string SourceAddress,
    string DataType,
    int? SamplingIntervalMs,
    double ScalingFactor,
    double ScalingOffset,
    bool Required,
    bool Enabled,
    IReadOnlyDictionary<string, string>? ValueMapping,
    IReadOnlyDictionary<string, object>? Options);
