namespace Rostek.Gateway.Contracts.Configuration;

public sealed record EffectiveMachineConfiguration(
    Guid MachineId,
    string MachineCode,
    string MachineName,
    string Protocol,
    bool Enabled,
    EffectiveConnectionConfiguration Connection,
    IReadOnlyList<EffectiveSignalConfiguration> Signals);
