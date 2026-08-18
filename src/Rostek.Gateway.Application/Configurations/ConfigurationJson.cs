using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rostek.Gateway.Contracts.Configuration;

namespace Rostek.Gateway.Application.Configurations;

internal static class ConfigurationJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(RuntimeConfiguration configuration)
    {
        var document = new ConfigurationExportDocument(
            SchemaVersion: 1,
            ConfigVersion: configuration.Version,
            AppliedAtUtc: configuration.AppliedAtUtc,
            Machines: configuration.Machines.Values.OrderBy(machine => machine.MachineCode, StringComparer.OrdinalIgnoreCase).ToList());

        return JsonSerializer.Serialize(document, SerializerOptions);
    }

    public static RuntimeConfiguration Deserialize(string json)
    {
        var document = JsonSerializer.Deserialize<ConfigurationExportDocument>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Configuration snapshot is empty.");

        return new RuntimeConfiguration(
            document.ConfigVersion,
            document.AppliedAtUtc,
            document.Machines.ToDictionary(machine => machine.MachineCode, StringComparer.OrdinalIgnoreCase));
    }

    public static string Checksum(string json)
    {
        var compact = JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(json), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(compact));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static IReadOnlyDictionary<string, object>? ParseObjectOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Options JSON must be an object.");
        }

        return document.RootElement.EnumerateObject()
            .Where(property => !IsSecretName(property.Name))
            .ToDictionary(property => property.Name, property => ToObject(property.Value), StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyDictionary<string, string>? ParseStringMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, SerializerOptions);
    }

    public static bool LooksSecretFree(string json)
    {
        var lowered = json.ToLowerInvariant();
        return !new[] { "password", "privatekey", "private_key", "access_token", "accesstoken", "secret" }.Any(lowered.Contains);
    }

    private static object ToObject(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => element.GetRawText()
        };

    private static bool IsSecretName(string name) =>
        name.Contains("password", StringComparison.OrdinalIgnoreCase)
        || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || name.Contains("token", StringComparison.OrdinalIgnoreCase)
        || name.Contains("privateKey", StringComparison.OrdinalIgnoreCase)
        || name.Contains("private_key", StringComparison.OrdinalIgnoreCase);
}
