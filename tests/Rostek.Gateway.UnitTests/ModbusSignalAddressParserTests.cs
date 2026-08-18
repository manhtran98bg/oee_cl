using Rostek.Gateway.Runtime.Modbus;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class ModbusSignalAddressParserTests
{
    private readonly ModbusSignalAddressParser _parser = new();

    [Theory]
    [InlineData("HR:40001", "Int16", ModbusAddressArea.HoldingRegister, 0, 1)]
    [InlineData("HOLDING:40002", "Float", ModbusAddressArea.HoldingRegister, 1, 2)]
    [InlineData("IR:30001", "Float", ModbusAddressArea.InputRegister, 0, 2)]
    [InlineData("COIL:00001", "Boolean", ModbusAddressArea.Coil, 0, 1)]
    [InlineData("DI:10001", "Boolean", ModbusAddressArea.DiscreteInput, 0, 1)]
    public void Parses_document_style_addresses(string sourceAddress, string dataType, ModbusAddressArea area, ushort offset, ushort pointCount)
    {
        var parsed = _parser.Parse(sourceAddress, dataType);

        Assert.Equal(area, parsed.Area);
        Assert.Equal(offset, parsed.Offset);
        Assert.Equal(pointCount, parsed.PointCount);
    }

    [Theory]
    [InlineData("HR40001")]
    [InlineData("HR:1")]
    [InlineData("COIL:40001")]
    [InlineData("UNKNOWN:40001")]
    public void Rejects_invalid_addresses(string sourceAddress)
    {
        Assert.Throws<FormatException>(() => _parser.Parse(sourceAddress, "Int16"));
    }

    [Fact]
    public void Rejects_register_type_on_bit_area()
    {
        Assert.Throws<FormatException>(() => _parser.Parse("COIL:00001", "Int16"));
    }
}
