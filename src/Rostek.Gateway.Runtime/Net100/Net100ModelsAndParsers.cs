using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Rostek.Gateway.Runtime.Net100;

public sealed record Net100LastShotInfo(
    long ShotNumber,
    string Date,
    string Time,
    int QualityCode,
    double CycleTimeSeconds);

public sealed record Net100LiveData(
    string Address,
    string? ConditionName,
    string Status,
    bool Alarm,
    string? Message,
    long ShotNumber,
    Net100LastShotInfo? LastShotInfo,
    string? Updated,
    bool UpdatingDatabase);

public sealed class Net100LastShotInfoParser
{
    public Net100LastShotInfo Parse(string csvRow)
    {
        if (string.IsNullOrWhiteSpace(csvRow))
        {
            throw new FormatException("NET100 lastshotinfo is empty.");
        }

        var fields = ParseFields(csvRow.Trim());
        if (fields.Count < 5)
        {
            throw new FormatException($"NET100 lastshotinfo must contain at least 5 columns, but {fields.Count} were found.");
        }

        if (!long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var shotNumber))
        {
            throw new FormatException("NET100 lastshotinfo Shot # is invalid.");
        }

        if (!int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var qualityCode))
        {
            throw new FormatException("NET100 lastshotinfo Quality Code is invalid.");
        }

        if (!double.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var cycleTimeSeconds))
        {
            throw new FormatException("NET100 lastshotinfo Cycle time is invalid.");
        }

        return new Net100LastShotInfo(shotNumber, fields[1], fields[2], qualityCode, cycleTimeSeconds);
    }

    private static IReadOnlyList<string> ParseFields(string row)
    {
        var fields = new List<string>();
        var value = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < row.Length; index++)
        {
            var character = row[index];
            if (character == '"')
            {
                if (quoted && index + 1 < row.Length && row[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }

                continue;
            }

            if (character == ',' && !quoted)
            {
                fields.Add(value.ToString().Trim());
                value.Clear();
                continue;
            }

            value.Append(character);
        }

        if (quoted)
        {
            throw new FormatException("NET100 lastshotinfo contains an unterminated quoted field.");
        }

        fields.Add(value.ToString().Trim());
        return fields;
    }
}

public sealed class Net100LiveParser(Net100LastShotInfoParser lastShotInfoParser)
{
    public Net100LiveData Parse(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (Exception exception)
        {
            throw new FormatException("NET100 live response is not valid XML.", exception);
        }

        var root = document.Root;
        if (root is null || !root.Name.LocalName.Equals("live", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("NET100 live response does not contain a live root element.");
        }

        string? Value(string name) => root.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim();

        var address = Value("address");
        var status = Value("status");
        var shotNumberText = Value("shotno");
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(status))
        {
            throw new FormatException("NET100 live response requires address and status.");
        }

        if (!long.TryParse(shotNumberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shotNumber))
        {
            throw new FormatException("NET100 live shotno is invalid.");
        }

        var alarm = bool.TryParse(Value("alarm"), out var parsedAlarm) && parsedAlarm;
        var updatingDatabase = bool.TryParse(Value("updatingdatabase"), out var parsedUpdating) && parsedUpdating;
        var lastShotInfoText = Value("lastshotinfo");
        Net100LastShotInfo? lastShotInfo = null;
        if (!string.IsNullOrWhiteSpace(lastShotInfoText))
        {
            lastShotInfo = lastShotInfoParser.Parse(lastShotInfoText);
        }

        return new Net100LiveData(
            address,
            Value("condname"),
            status,
            alarm,
            Value("message"),
            shotNumber,
            lastShotInfo,
            Value("updated"),
            updatingDatabase);
    }
}
