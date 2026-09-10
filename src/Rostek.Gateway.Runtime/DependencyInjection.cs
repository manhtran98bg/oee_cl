using Microsoft.Extensions.DependencyInjection;
using Rostek.Gateway.Contracts.Runtime;
using Rostek.Gateway.Runtime.Configuration;
using Rostek.Gateway.Runtime.Machines;
using Rostek.Gateway.Runtime.Modbus;
using Rostek.Gateway.Runtime.OpcUa;

namespace Rostek.Gateway.Runtime;

public static class DependencyInjection
{
    public static IServiceCollection AddGatewayRuntime(this IServiceCollection services)
    {
        services.AddSingleton<RuntimeConfigurationProvider>();
        services.AddSingleton<IRuntimeConfigurationProvider>(sp => sp.GetRequiredService<RuntimeConfigurationProvider>());
        services.AddSingleton<UnsupportedProtocolMachineRuntimeFactory>();
        services.AddSingleton<ModbusTcpMachineRuntimeFactory>();
        services.AddSingleton<OpcUaMachineRuntimeFactory>();
        services.AddSingleton<IMachineRuntimeFactory, ProtocolMachineRuntimeFactory>();
        services.AddSingleton<IModbusTcpClientAdapterFactory, NModbusTcpClientAdapterFactory>();
        services.AddSingleton<IOpcUaClientAdapterFactory, OpcUaClientAdapterFactory>();
        services.AddSingleton<ModbusSignalAddressParser>();
        services.AddSingleton<ModbusValueDecoder>();
        services.AddSingleton<OpcUaValueConverter>();
        services.AddSingleton<MachineValueStore>();
        services.AddSingleton<IMachineValueReader>(sp => sp.GetRequiredService<MachineValueStore>());
        services.AddSingleton<ConfigurationDiffService>();
        services.AddSingleton<MachineRuntimeManager>();
        services.AddSingleton<IMachineRuntimeManager>(sp => sp.GetRequiredService<MachineRuntimeManager>());
        services.AddSingleton<IRuntimeStatusReader>(sp => sp.GetRequiredService<MachineRuntimeManager>());
        return services;
    }
}
