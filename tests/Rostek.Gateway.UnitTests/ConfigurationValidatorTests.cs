using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.Configurations;
using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.Runtime;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class ConfigurationValidatorTests
{
    [Fact]
    public async Task Required_signal_cannot_be_disabled()
    {
        var configuration = CreateConfiguration(signalEnabled: false, signalRequired: true);
        var validator = new ConfigurationValidator(Options.Create(new RuntimeOptions()));

        var result = await validator.ValidateAsync(configuration, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "REQUIRED_SIGNAL_DISABLED");
    }

    [Fact]
    public async Task Opcua_endpoint_must_use_opc_tcp()
    {
        var configuration = CreateConfiguration(endpointUrl: "http://localhost:4840");
        var validator = new ConfigurationValidator(Options.Create(new RuntimeOptions()));

        var result = await validator.ValidateAsync(configuration, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "OPCUA_ENDPOINT_INVALID");
    }

    [Fact]
    public async Task Opcua_v1_requires_anonymous_no_security()
    {
        var configuration = CreateConfiguration(securityMode: "Sign");
        var validator = new ConfigurationValidator(Options.Create(new RuntimeOptions()));

        var result = await validator.ValidateAsync(configuration, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "OPCUA_SECURITY_UNSUPPORTED");
    }

    [Fact]
    public async Task Opcua_signal_address_must_be_node_id()
    {
        var configuration = CreateConfiguration(sourceAddress: "cuulong.counter");
        var validator = new ConfigurationValidator(Options.Create(new RuntimeOptions()));

        var result = await validator.ValidateAsync(configuration, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "OPCUA_SIGNAL_NODEID_INVALID");
    }

    [Fact]
    public async Task Modbus_signal_address_must_use_supported_area_and_range()
    {
        var configuration = CreateModbusConfiguration("HR:1", "Int16");
        var validator = new ConfigurationValidator(Options.Create(new RuntimeOptions()));

        var result = await validator.ValidateAsync(configuration, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "MODBUS_SIGNAL_ADDRESS_RANGE_INVALID");
    }

    private static RuntimeConfiguration CreateConfiguration(
        string endpointUrl = "opc.tcp://127.0.0.1:4840",
        bool signalEnabled = true,
        bool signalRequired = true,
        string sourceAddress = "ns=2;s=Machine.State",
        string securityMode = "NONE")
    {
        var machine = new EffectiveMachineConfiguration(
            Guid.NewGuid(),
            "M16-01",
            "Machine 01",
            "OPCUA",
            true,
            new EffectiveConnectionConfiguration("OPCUA", null, null, endpointUrl, null, securityMode, "None", "ANONYMOUS", null, 3000, 3000, 3, 1000, null),
            [
                new EffectiveSignalConfiguration("machine_state", sourceAddress, "INT16", 1000, 1, 0, signalRequired, signalEnabled, null, null)
            ]);

        return new RuntimeConfiguration(1, DateTimeOffset.UtcNow, new Dictionary<string, EffectiveMachineConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            [machine.MachineCode] = machine
        });
    }

    private static RuntimeConfiguration CreateModbusConfiguration(string sourceAddress, string dataType)
    {
        var machine = new EffectiveMachineConfiguration(
            Guid.NewGuid(),
            "M16-02",
            "Machine 02",
            "MODBUS_TCP",
            true,
            new EffectiveConnectionConfiguration("MODBUS_TCP", "127.0.0.1", 502, null, 1, null, null, null, null, 3000, 3000, 3, 1000, null),
            [
                new EffectiveSignalConfiguration("speed", sourceAddress, dataType, 1000, 1, 0, true, true, null, null)
            ]);

        return new RuntimeConfiguration(1, DateTimeOffset.UtcNow, new Dictionary<string, EffectiveMachineConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            [machine.MachineCode] = machine
        });
    }
}
