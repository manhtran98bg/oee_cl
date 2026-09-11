using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.Runtime;
using Microsoft.Extensions.Options;

namespace Rostek.Gateway.Application.Configurations;

public sealed class ConfigurationValidator(IOptions<RuntimeOptions> runtimeOptions) : IConfigurationValidator
{
    public Task<ConfigurationValidationResult> ValidateAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken)
    {
        var issues = new List<ConfigurationValidationIssue>();
        var machineCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var machine in configuration.Machines.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!machineCodes.Add(machine.MachineCode))
            {
                AddError(issues, "DUPLICATE_MACHINE_CODE", $"Duplicate machine code '{machine.MachineCode}'.", machine.MachineCode, null, "MachineCode");
            }

            if (string.IsNullOrWhiteSpace(machine.MachineCode))
            {
                AddError(issues, "MACHINE_CODE_REQUIRED", "Machine code is required.", machine.MachineCode, null, "MachineCode");
            }

            if (!machine.Enabled)
            {
                continue;
            }

            ValidateConnection(machine, issues);
            foreach (var signal in machine.Signals)
            {
                ValidateSignal(machine, signal, issues);
            }
        }

        return Task.FromResult(new ConfigurationValidationResult(!issues.Any(issue => issue.Severity == "ERROR"), issues));
    }

    private void ValidateConnection(EffectiveMachineConfiguration machine, List<ConfigurationValidationIssue> issues)
    {
        var oeeTimeSource = OeeTimeSources.FromOptions(machine.Connection.Options);
        if (!OeeTimeSources.IsValid(oeeTimeSource))
        {
            AddError(issues, "OEE_TIME_SOURCE_INVALID", "OEE time source must be device_counters, gateway_state, or auto.", machine.MachineCode, null, OeeTimeSources.OptionName);
        }

        if (machine.Connection.ConnectTimeoutMs <= 0 || machine.Connection.RequestTimeoutMs <= 0)
        {
            AddError(issues, "TIMEOUT_INVALID", "Timeout must be greater than zero.", machine.MachineCode, null, "Timeout");
        }

        if ((machine.Connection.PollingIntervalMs ?? runtimeOptions.Value.MinimumPollingIntervalMs) < runtimeOptions.Value.MinimumPollingIntervalMs)
        {
            AddError(issues, "POLLING_INTERVAL_TOO_LOW", $"Polling interval must be at least {runtimeOptions.Value.MinimumPollingIntervalMs} ms.", machine.MachineCode, null, "PollingIntervalMs");
        }

        if (IsOpcUa(machine.Protocol))
        {
            if (!Uri.TryCreate(machine.Connection.EndpointUrl, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "opc.tcp")
            {
                AddError(issues, "OPCUA_ENDPOINT_INVALID", "OPC UA endpoint must be an absolute opc.tcp URI.", machine.MachineCode, null, "EndpointUrl");
            }

            if (!IsAnonymousNoSecurity(machine.Connection))
            {
                AddError(issues, "OPCUA_SECURITY_UNSUPPORTED", "OPC UA V1 only supports anonymous connection with SecurityMode NONE and SecurityPolicy None.", machine.MachineCode, null, "SecurityMode");
            }
        }
        else if (machine.Protocol == "MODBUSTCP" || machine.Protocol == "MODBUS_TCP")
        {
            if (string.IsNullOrWhiteSpace(machine.Connection.Host))
            {
                AddError(issues, "MODBUS_HOST_REQUIRED", "Modbus host is required.", machine.MachineCode, null, "Host");
            }

            if (machine.Connection.Port is < 1 or > 65535)
            {
                AddError(issues, "PORT_INVALID", "Port must be between 1 and 65535.", machine.MachineCode, null, "Port");
            }

            if (machine.Connection.UnitId is null or < 0 or > 255)
            {
                AddError(issues, "MODBUS_UNIT_INVALID", "Unit ID must be between 0 and 255.", machine.MachineCode, null, "UnitId");
            }
        }
    }

    private static void ValidateSignal(EffectiveMachineConfiguration machine, EffectiveSignalConfiguration signal, List<ConfigurationValidationIssue> issues)
    {
        if (signal.Required && !signal.Enabled)
        {
            AddError(issues, "REQUIRED_SIGNAL_DISABLED", "Required signal cannot be disabled.", machine.MachineCode, signal.SignalCode, "Enabled");
        }

        if (signal.Enabled && string.IsNullOrWhiteSpace(signal.SourceAddress))
        {
            AddError(issues, "SIGNAL_SOURCE_REQUIRED", "Enabled signal source address is required.", machine.MachineCode, signal.SignalCode, "SourceAddress");
        }

        if (double.IsNaN(signal.ScalingFactor) || double.IsInfinity(signal.ScalingFactor))
        {
            AddError(issues, "SCALING_INVALID", "Scaling factor cannot be NaN or Infinity.", machine.MachineCode, signal.SignalCode, "ScalingFactor");
        }

        if (signal.Enabled && IsModbusTcp(machine.Protocol) && !string.IsNullOrWhiteSpace(signal.SourceAddress))
        {
            ValidateModbusSignal(machine.MachineCode, signal, issues);
        }

        if (signal.Enabled && IsOpcUa(machine.Protocol) && !string.IsNullOrWhiteSpace(signal.SourceAddress))
        {
            ValidateOpcUaSignal(machine.MachineCode, signal, issues);
        }
    }

    private static void ValidateOpcUaSignal(string machineCode, EffectiveSignalConfiguration signal, List<ConfigurationValidationIssue> issues)
    {
        var parts = signal.SourceAddress.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var hasNamespace = parts.Any(part => part.StartsWith("ns=", StringComparison.OrdinalIgnoreCase) &&
                                             int.TryParse(part[3..], out var ns) &&
                                             ns >= 0);
        var hasIdentifier = parts.Any(part =>
            part.Length > 2 &&
            (part.StartsWith("s=", StringComparison.OrdinalIgnoreCase) ||
             part.StartsWith("i=", StringComparison.OrdinalIgnoreCase) ||
             part.StartsWith("g=", StringComparison.OrdinalIgnoreCase) ||
             part.StartsWith("b=", StringComparison.OrdinalIgnoreCase)));

        if (!hasNamespace || !hasIdentifier)
        {
            AddError(issues, "OPCUA_SIGNAL_NODEID_INVALID", "OPC UA signal source address must be a NodeId such as ns=1;s=cuulong.counter.", machineCode, signal.SignalCode, "SourceAddress");
        }
    }

    private static void ValidateModbusSignal(string machineCode, EffectiveSignalConfiguration signal, List<ConfigurationValidationIssue> issues)
    {
        var parts = signal.SourceAddress.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var address))
        {
            AddError(issues, "MODBUS_SIGNAL_ADDRESS_INVALID", "Modbus signal address must use AREA:ADDRESS format.", machineCode, signal.SignalCode, "SourceAddress");
            return;
        }

        var area = parts[0].Trim().ToUpperInvariant();
        var dataType = signal.DataType.Trim().ToUpperInvariant();
        var validArea = area is "COIL" or "COILS" or "DI" or "DISCRETE" or "DISCRETE_INPUT" or "DISCRETEINPUT" or "HR" or "HOLDING" or "HOLDING_REGISTER" or "HOLDINGREGISTER" or "IR" or "INPUT" or "INPUT_REGISTER" or "INPUTREGISTER";
        if (!validArea)
        {
            AddError(issues, "MODBUS_SIGNAL_AREA_INVALID", "Modbus signal area must be HR, HOLDING, IR, INPUT, COIL, DI, or DISCRETE.", machineCode, signal.SignalCode, "SourceAddress");
            return;
        }

        var isBitArea = area is "COIL" or "COILS" or "DI" or "DISCRETE" or "DISCRETE_INPUT" or "DISCRETEINPUT";
        if (isBitArea && dataType != "BOOLEAN")
        {
            AddError(issues, "MODBUS_BIT_TYPE_INVALID", "Coil and discrete input addresses only support Boolean signals.", machineCode, signal.SignalCode, "DataType");
        }

        if (!isBitArea && dataType is not ("INT16" or "INT32" or "FLOAT" or "DOUBLE"))
        {
            AddError(issues, "MODBUS_REGISTER_TYPE_INVALID", "Register addresses support Int16, Int32, Float, and Double signals.", machineCode, signal.SignalCode, "DataType");
        }

        var inRange = area switch
        {
            "COIL" or "COILS" => address is >= 1 and <= 65536,
            "DI" or "DISCRETE" or "DISCRETE_INPUT" or "DISCRETEINPUT" => address is >= 10001 and <= 75536,
            "HR" or "HOLDING" or "HOLDING_REGISTER" or "HOLDINGREGISTER" => address is >= 40001 and <= 105536,
            "IR" or "INPUT" or "INPUT_REGISTER" or "INPUTREGISTER" => address is >= 30001 and <= 95536,
            _ => false
        };

        if (!inRange)
        {
            AddError(issues, "MODBUS_SIGNAL_ADDRESS_RANGE_INVALID", "Modbus signal address is outside the supported range.", machineCode, signal.SignalCode, "SourceAddress");
        }
    }

    private static bool IsModbusTcp(string protocol) =>
        protocol.Equals("MODBUS_TCP", StringComparison.OrdinalIgnoreCase) ||
        protocol.Equals("MODBUSTCP", StringComparison.OrdinalIgnoreCase);

    private static bool IsOpcUa(string protocol) =>
        protocol.Equals("OPCUA", StringComparison.OrdinalIgnoreCase) ||
        protocol.Equals("OPC_UA", StringComparison.OrdinalIgnoreCase);

    private static bool IsAnonymousNoSecurity(EffectiveConnectionConfiguration connection)
    {
        var securityMode = string.IsNullOrWhiteSpace(connection.SecurityMode) ? "NONE" : connection.SecurityMode;
        var securityPolicy = string.IsNullOrWhiteSpace(connection.SecurityPolicy) ? "None" : connection.SecurityPolicy;
        var authenticationMode = string.IsNullOrWhiteSpace(connection.AuthenticationMode) ? "ANONYMOUS" : connection.AuthenticationMode;

        return securityMode.Equals("NONE", StringComparison.OrdinalIgnoreCase) &&
               securityPolicy.Equals("None", StringComparison.OrdinalIgnoreCase) &&
               authenticationMode.Equals("ANONYMOUS", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddError(List<ConfigurationValidationIssue> issues, string code, string message, string? machineCode, string? signalCode, string? field) =>
        issues.Add(new ConfigurationValidationIssue("ERROR", code, message, machineCode, signalCode, field));
}
