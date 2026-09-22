using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.Runtime;

namespace Rostek.Gateway.UnitTests.Support;

public sealed class TestRuntimeConfigurationProvider : IRuntimeConfigurationProvider
{
    public TestRuntimeConfigurationProvider(params (string MachineCode, bool Enabled)[] machines)
    {
        var configurations = machines.Select(machine => CreateMachine(machine.MachineCode, machine.Enabled))
            .ToDictionary(machine => machine.MachineCode, StringComparer.OrdinalIgnoreCase);
        Current = new RuntimeConfiguration(1, DateTimeOffset.UtcNow, configurations);
    }

    public RuntimeConfiguration Current { get; private set; }

    public Task ReplaceAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken)
    {
        Current = configuration;
        return Task.CompletedTask;
    }

    private static EffectiveMachineConfiguration CreateMachine(string machineCode, bool enabled) =>
        new(
            Guid.NewGuid(),
            machineCode,
            machineCode,
            "OPCUA",
            enabled,
            new EffectiveConnectionConfiguration(
                "OPCUA",
                null,
                null,
                "opc.tcp://localhost:4840",
                null,
                "NONE",
                "None",
                "ANONYMOUS",
                null,
                3000,
                3000,
                0,
                1000,
                null),
            []);
}
