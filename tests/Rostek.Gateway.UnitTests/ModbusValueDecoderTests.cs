using Rostek.Gateway.Runtime.Modbus;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class ModbusValueDecoderTests
{
    private readonly ModbusValueDecoder _decoder = new();

    [Fact]
    public void Decodes_boolean_from_bit_area()
    {
        var address = new ModbusSignalAddress(ModbusAddressArea.Coil, 0, 1);

        var value = _decoder.Decode(address, "Boolean", [], [true]);

        Assert.Equal(true, value);
    }

    [Fact]
    public void Decodes_int16_from_single_register()
    {
        var address = new ModbusSignalAddress(ModbusAddressArea.HoldingRegister, 0, 1);

        var value = _decoder.Decode(address, "Int16", [0xFFFF], []);

        Assert.Equal((short)-1, value);
    }

    [Fact]
    public void Decodes_float_from_two_little_endian_registers()
    {
        var address = new ModbusSignalAddress(ModbusAddressArea.HoldingRegister, 0, 2);

        var value = _decoder.Decode(address, "Float", [0x0000, 0x42C8], []);

        Assert.Equal(100f, Assert.IsType<float>(value));
    }
}
