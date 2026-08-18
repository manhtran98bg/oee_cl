using System.Buffers.Binary;

namespace Rostek.Gateway.Runtime.Modbus;

public sealed class ModbusValueDecoder
{
    public object Decode(ModbusSignalAddress address, string dataType, ushort[] registers, bool[] bits)
    {
        var normalizedType = dataType.Trim().ToUpperInvariant();
        if (address.Area is ModbusAddressArea.Coil or ModbusAddressArea.DiscreteInput)
        {
            EnsureBitCount(bits);
            return bits[0];
        }

        EnsureRegisterCount(registers, address.PointCount);
        switch (normalizedType)
        {
            case "INT16":
                return unchecked((short)registers[0]);
            case "INT32":
                return BinaryPrimitives.ReadInt32LittleEndian(ToBytes(registers, 2));
            case "FLOAT":
                return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(ToBytes(registers, 2)));
            case "DOUBLE":
                return BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(ToBytes(registers, 4)));
            default:
                throw new FormatException("Unsupported Modbus signal data type.");
        }
    }

    private static byte[] ToBytes(ushort[] registers, int registerCount)
    {
        var bytes = new byte[registerCount * 2];
        for (var index = 0; index < registerCount; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * 2, 2), registers[index]);
        }

        return bytes;
    }

    private static void EnsureRegisterCount(ushort[] registers, ushort expectedCount)
    {
        if (registers.Length < expectedCount)
        {
            throw new FormatException("Modbus register response is shorter than expected.");
        }
    }

    private static void EnsureBitCount(bool[] bits)
    {
        if (bits.Length == 0)
        {
            throw new FormatException("Modbus bit response is empty.");
        }
    }
}
