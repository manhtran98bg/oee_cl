namespace Rostek.Gateway.Contracts.Configuration;

public sealed record ConfigurationExportDocument(
    int SchemaVersion,
    long ConfigVersion,
    DateTimeOffset AppliedAtUtc,
    IReadOnlyList<EffectiveMachineConfiguration> Machines);
