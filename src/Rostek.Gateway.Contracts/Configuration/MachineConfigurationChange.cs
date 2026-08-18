namespace Rostek.Gateway.Contracts.Configuration;

public enum MachineConfigurationChangeType
{
    Unchanged,
    Added,
    Removed,
    Enabled,
    Disabled,
    ConnectionChanged,
    SignalsChanged,
    TemplateChanged
}

public sealed record MachineConfigurationChange(
    string MachineCode,
    MachineConfigurationChangeType ChangeType,
    EffectiveMachineConfiguration? Previous,
    EffectiveMachineConfiguration? Current);
