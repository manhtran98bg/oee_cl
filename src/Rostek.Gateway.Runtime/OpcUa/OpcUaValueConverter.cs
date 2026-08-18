using System.Globalization;

namespace Rostek.Gateway.Runtime.OpcUa;

public sealed class OpcUaValueConverter
{
    public object? ConvertValue(object? value, string dataType)
    {
        if (value is null)
        {
            return null;
        }

        if (value is Array)
        {
            throw new NotSupportedException("OPC UA array values are not supported in V1.");
        }

        return dataType.Trim().ToUpperInvariant() switch
        {
            "BOOLEAN" or "BOOL" => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
            "INT16" => Convert.ToInt16(value, CultureInfo.InvariantCulture),
            "INT32" => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            "FLOAT" => Convert.ToSingle(value, CultureInfo.InvariantCulture),
            "DOUBLE" => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            "STRING" => Convert.ToString(value, CultureInfo.InvariantCulture),
            _ => throw new NotSupportedException($"OPC UA data type '{dataType}' is not supported.")
        };
    }
}
