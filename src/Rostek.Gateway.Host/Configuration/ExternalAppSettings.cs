using System.Text.Json;

namespace Rostek.Gateway.Host.Configuration;

public sealed record ExternalAppSettingsStatus(
    string GatewayHomePath,
    string ConfigPath,
    bool Created);

public static class ExternalAppSettings
{
    private const string GatewayHomeDirectoryName = ".gateway";
    private const string AppSettingsFileName = "appsettings.json";

    public static ExternalAppSettingsStatus Ensure(string? userHomePath = null)
    {
        var gatewayHomePath = ResolveGatewayHomePath(userHomePath);
        Directory.CreateDirectory(gatewayHomePath);

        var configPath = Path.Combine(gatewayHomePath, AppSettingsFileName);
        if (File.Exists(configPath))
        {
            return new ExternalAppSettingsStatus(gatewayHomePath, configPath, Created: false);
        }

        File.WriteAllText(configPath, CreateDefaultContent());
        return new ExternalAppSettingsStatus(gatewayHomePath, configPath, Created: true);
    }

    public static string ResolveGatewayHomePath(string? userHomePath = null)
    {
        var home = string.IsNullOrWhiteSpace(userHomePath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : userHomePath;

        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetEnvironmentVariable("HOME") ?? AppContext.BaseDirectory;
        }

        return Path.Combine(home, GatewayHomeDirectoryName);
    }

    private static string CreateDefaultContent()
    {
        var content = new
        {
            Gateway = new
            {
                GatewayId = "GW-M16-01",
                DataDirectory = "data",
                BackupDirectory = "backups",
                ExportDirectory = "exports"
            },
            Runtime = new
            {
                MaxInitialConcurrentConnections = 5,
                MaxReconnectConcurrentConnections = 5,
                MinimumPollingIntervalMs = 200,
                MaxReconnectBackoffMs = 30000,
                ShutdownTimeoutSeconds = 30
            },
            Kestrel = new
            {
                Endpoints = new
                {
                    Http = new
                    {
                        Url = "http://0.0.0.0:8080"
                    }
                }
            },
            History = new
            {
                Enabled = false,
                Provider = "Postgres",
                SampleIntervalMs = 5000,
                BatchSize = 500,
                CommandTimeoutSeconds = 10,
                ConnectionString = string.Empty,
                RetentionDays = 90
            }
        };

        return JsonSerializer.Serialize(content, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }
}
