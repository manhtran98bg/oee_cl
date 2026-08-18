using Rostek.Gateway.Runtime.OpcUa;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class OpcUaValueConverterTests
{
    private readonly OpcUaValueConverter _converter = new();

    [Theory]
    [InlineData(12, "Int16", typeof(short))]
    [InlineData(12, "Int32", typeof(int))]
    [InlineData(12.5, "Float", typeof(float))]
    [InlineData(12.5, "Double", typeof(double))]
    [InlineData(1, "Boolean", typeof(bool))]
    [InlineData(123, "String", typeof(string))]
    public void Converts_supported_scalar_values(object value, string dataType, Type expectedType)
    {
        var converted = _converter.ConvertValue(value, dataType);

        Assert.IsType(expectedType, converted);
    }

    [Fact]
    public void Rejects_array_values_in_v1()
    {
        var ex = Assert.Throws<NotSupportedException>(() => _converter.ConvertValue(new[] { 1, 2 }, "Int32"));

        Assert.Contains("array", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_unsupported_data_type()
    {
        var ex = Assert.Throws<NotSupportedException>(() => _converter.ConvertValue(123, "DateTime"));

        Assert.Contains("DateTime", ex.Message);
    }
}
