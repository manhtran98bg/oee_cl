using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Runtime.Configuration;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class ConfigurationDiffServiceTests
{
    [Fact]
    public void Connection_change_reloads_only_changed_machine()
    {
        var previous = CreateConfiguration("opc.tcp://127.0.0.1:4840");
        var current = CreateConfiguration("opc.tcp://127.0.0.2:4840");

        var changes = new ConfigurationDiffService().Diff(previous, current);

        Assert.Single(changes);
        Assert.Equal(MachineConfigurationChangeType.ConnectionChanged, changes[0].ChangeType);
        Assert.Equal("M16-01", changes[0].MachineCode);
    }

    private static RuntimeConfiguration CreateConfiguration(string endpoint)
    {
        var machine = new EffectiveMachineConfiguration(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "M16-01",
            "Machine 01",
            "OPCUA",
            true,
            new EffectiveConnectionConfiguration("OPCUA", null, null, endpoint, null, "NONE", "None", "ANONYMOUS", null, 3000, 3000, 3, 1000, null),
            [new EffectiveSignalConfiguration("machine_state", "ns=2;s=Machine.State", "INT16", 1000, 1, 0, true, true, null, null)]);

        return new RuntimeConfiguration(1, DateTimeOffset.UtcNow, new Dictionary<string, EffectiveMachineConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            [machine.MachineCode] = machine
        });
    }
}
