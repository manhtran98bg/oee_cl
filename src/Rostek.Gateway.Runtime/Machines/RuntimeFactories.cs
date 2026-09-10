using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.Runtime;
using Rostek.Gateway.Runtime.Modbus;
using Rostek.Gateway.Runtime.OpcUa;

namespace Rostek.Gateway.Runtime.Machines;

public sealed class ModbusTcpMachineRuntimeFactory(
    IModbusTcpClientAdapterFactory adapterFactory,
    ModbusSignalAddressParser addressParser,
    ModbusValueDecoder valueDecoder,
    MachineValueStore valueStore,
    IOptions<RuntimeOptions> options,
    ILogger<ModbusTcpMachineRuntime> logger)
{
    public IMachineRuntime Create(EffectiveMachineConfiguration configuration) =>
        new ModbusTcpMachineRuntime(configuration, adapterFactory, addressParser, valueDecoder, valueStore, options, logger);
}

public sealed class OpcUaMachineRuntimeFactory(
    IOpcUaClientAdapterFactory adapterFactory,
    OpcUaValueConverter valueConverter,
    MachineValueStore valueStore,
    IOptions<RuntimeOptions> options,
    ILogger<OpcUaMachineRuntime> logger)
{
    public IMachineRuntime Create(EffectiveMachineConfiguration configuration) =>
        new OpcUaMachineRuntime(configuration, adapterFactory, valueConverter, valueStore, options, logger);
}

public sealed class ProtocolMachineRuntimeFactory(
    UnsupportedProtocolMachineRuntimeFactory unsupportedProtocolFactory,
    ModbusTcpMachineRuntimeFactory modbusTcpFactory,
    OpcUaMachineRuntimeFactory opcUaFactory) : IMachineRuntimeFactory
{
    public IMachineRuntime Create(EffectiveMachineConfiguration configuration) =>
        IsModbusTcp(configuration.Protocol)
            ? modbusTcpFactory.Create(configuration)
            : IsOpcUa(configuration.Protocol)
                ? opcUaFactory.Create(configuration)
                : unsupportedProtocolFactory.Create(configuration);

    private static bool IsModbusTcp(string protocol) =>
        protocol.Equals("MODBUS_TCP", StringComparison.OrdinalIgnoreCase) ||
        protocol.Equals("MODBUSTCP", StringComparison.OrdinalIgnoreCase);

    private static bool IsOpcUa(string protocol) =>
        protocol.Equals("OPCUA", StringComparison.OrdinalIgnoreCase) ||
        protocol.Equals("OPC_UA", StringComparison.OrdinalIgnoreCase);
}
