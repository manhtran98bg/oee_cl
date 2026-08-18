using Rostek.Gateway.Contracts.Configuration;

namespace Rostek.Gateway.Application.Configurations;

public sealed class ConfigurationDiffService
{
    public IReadOnlyList<MachineConfigurationChange> Diff(RuntimeConfiguration previous, RuntimeConfiguration current)
    {
        var changes = new List<MachineConfigurationChange>();
        var keys = previous.Machines.Keys.Concat(current.Machines.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            previous.Machines.TryGetValue(key, out var oldMachine);
            current.Machines.TryGetValue(key, out var newMachine);
            changes.Add(new MachineConfigurationChange(key, GetChangeType(oldMachine, newMachine), oldMachine, newMachine));
        }

        return changes;
    }

    private static MachineConfigurationChangeType GetChangeType(EffectiveMachineConfiguration? previous, EffectiveMachineConfiguration? current)
    {
        if (previous is null)
        {
            return MachineConfigurationChangeType.Added;
        }

        if (current is null)
        {
            return MachineConfigurationChangeType.Removed;
        }

        if (!previous.Enabled && current.Enabled)
        {
            return MachineConfigurationChangeType.Enabled;
        }

        if (previous.Enabled && !current.Enabled)
        {
            return MachineConfigurationChangeType.Disabled;
        }

        if (previous.Protocol != current.Protocol)
        {
            return MachineConfigurationChangeType.TemplateChanged;
        }

        if (previous.Connection != current.Connection)
        {
            return MachineConfigurationChangeType.ConnectionChanged;
        }

        return previous.Signals.SequenceEqual(current.Signals)
            ? MachineConfigurationChangeType.Unchanged
            : MachineConfigurationChangeType.SignalsChanged;
    }
}
