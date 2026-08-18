using System.Globalization;

namespace Rostek.Gateway.Runtime.Modbus;

public enum ModbusAddressArea
{
    Coil,
    DiscreteInput,
    HoldingRegister,
    InputRegister
}

public sealed record ModbusSignalAddress(
    ModbusAddressArea Area,
    ushort Offset,
    ushort PointCount);

public sealed class ModbusSignalAddressParser
{
    public ModbusSignalAddress Parse(string sourceAddress, string dataType)
    {
        if (string.IsNullOrWhiteSpace(sourceAddress))
        {
            throw new FormatException("Modbus address is required.");
        }

        var parts = sourceAddress.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            throw new FormatException("Modbus address must use AREA:ADDRESS format.");
        }

        var area = ParseArea(parts[0]);
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var address))
        {
            throw new FormatException("Modbus address must be numeric.");
        }

        var offset = ToZeroBasedOffset(area, address);
        var pointCount = GetPointCount(area, dataType);
        return new ModbusSignalAddress(area, offset, pointCount);
    }

    private static ModbusAddressArea ParseArea(string area) =>
        area.Trim().ToUpperInvariant() switch
        {
            "COIL" or "COILS" => ModbusAddressArea.Coil,
            "DI" or "DISCRETE" or "DISCRETE_INPUT" or "DISCRETEINPUT" => ModbusAddressArea.DiscreteInput,
            "HR" or "HOLDING" or "HOLDING_REGISTER" or "HOLDINGREGISTER" => ModbusAddressArea.HoldingRegister,
            "IR" or "INPUT" or "INPUT_REGISTER" or "INPUTREGISTER" => ModbusAddressArea.InputRegister,
            _ => throw new FormatException("Unknown Modbus address area.")
        };

    private static ushort ToZeroBasedOffset(ModbusAddressArea area, int address)
    {
        var offset = area switch
        {
            ModbusAddressArea.Coil => address - 1,
            ModbusAddressArea.DiscreteInput => address - 10001,
            ModbusAddressArea.HoldingRegister => address - 40001,
            ModbusAddressArea.InputRegister => address - 30001,
            _ => throw new ArgumentOutOfRangeException(nameof(area))
        };

        if (offset is < 0 or > ushort.MaxValue)
        {
            throw new FormatException("Modbus address is outside the supported range.");
        }

        return (ushort)offset;
    }

    private static ushort GetPointCount(ModbusAddressArea area, string dataType)
    {
        var normalizedType = dataType.Trim().ToUpperInvariant();
        var isBitArea = area is ModbusAddressArea.Coil or ModbusAddressArea.DiscreteInput;
        if (isBitArea)
        {
            return normalizedType == "BOOLEAN"
                ? (ushort)1
                : throw new FormatException("Coil and discrete input addresses only support Boolean signals.");
        }

        return normalizedType switch
        {
            "INT16" => 1,
            "INT32" or "FLOAT" => 2,
            "DOUBLE" => 4,
            _ => throw new FormatException("Register addresses support Int16, Int32, Float, and Double signals.")
        };
    }
}
