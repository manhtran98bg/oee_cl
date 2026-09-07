using Microsoft.Extensions.Configuration;
using Rostek.Gateway.Host.Configuration;
using Xunit;

namespace Rostek.Gateway.IntegrationTests;

public sealed class ExternalAppSettingsTests
{
    [Fact]
    public void Ensure_creates_default_external_appsettings_when_file_is_missing()
    {
        var home = CreateTempHome();

        var status = ExternalAppSettings.Ensure(home);

        Assert.True(status.Created);
        Assert.Equal(Path.Combine(home, ".gateway"), status.GatewayHomePath);
        Assert.True(File.Exists(status.ConfigPath));

        var content = File.ReadAllText(status.ConfigPath);
        Assert.Contains("\"Gateway\"", content);
        Assert.Contains("\"Runtime\"", content);
        Assert.Contains("\"Kestrel\"", content);
        Assert.Contains("\"MesSync\"", content);
        Assert.Contains("\"BaseUrl\": \"\"", content);
        Assert.Contains("\"SyncIntervalMs\": 5000", content);
        Assert.Contains("\"RequireProductionContext\": false", content);
    }

    [Fact]
    public void Ensure_does_not_overwrite_existing_external_appsettings()
    {
        var home = CreateTempHome();
        var gatewayHome = Path.Combine(home, ".gateway");
        Directory.CreateDirectory(gatewayHome);
        var configPath = Path.Combine(gatewayHome, "appsettings.json");
        File.WriteAllText(configPath, "{ \"Gateway\": { \"GatewayId\": \"CUSTOM\" } }");

        var status = ExternalAppSettings.Ensure(home);

        Assert.False(status.Created);
        Assert.Equal("{ \"Gateway\": { \"GatewayId\": \"CUSTOM\" } }", File.ReadAllText(configPath));
    }

    [Fact]
    public void Resolve_gateway_home_can_use_environment_variable()
    {
        var previous = Environment.GetEnvironmentVariable(ExternalAppSettings.GatewayHomeEnvironmentVariable);
        var gatewayHome = Path.Combine(Path.GetTempPath(), "ro-stek-gateway-tests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(ExternalAppSettings.GatewayHomeEnvironmentVariable, gatewayHome);

        try
        {
            Assert.Equal(gatewayHome, ExternalAppSettings.ResolveGatewayHomePath());
        }
        finally
        {
            Environment.SetEnvironmentVariable(ExternalAppSettings.GatewayHomeEnvironmentVariable, previous);
        }
    }


    [Fact]
    public void External_appsettings_overrides_default_and_environment_style_values_override_external()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ro-stek-gateway-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var defaultPath = Path.Combine(directory, "default.json");
        var externalPath = Path.Combine(directory, "external.json");
        File.WriteAllText(defaultPath, "{ \"Gateway\": { \"GatewayId\": \"DEFAULT\" } }");
        File.WriteAllText(externalPath, "{ \"Gateway\": { \"GatewayId\": \"EXTERNAL\" } }");

        var externalConfiguration = new ConfigurationBuilder()
            .AddJsonFile(defaultPath)
            .AddJsonFile(externalPath, optional: true, reloadOnChange: false)
            .Build();

        var environmentStyleConfiguration = new ConfigurationBuilder()
            .AddJsonFile(defaultPath)
            .AddJsonFile(externalPath, optional: true, reloadOnChange: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:GatewayId"] = "ENVIRONMENT"
            })
            .Build();

        Assert.Equal("EXTERNAL", externalConfiguration["Gateway:GatewayId"]);
        Assert.Equal("ENVIRONMENT", environmentStyleConfiguration["Gateway:GatewayId"]);
    }

    private static string CreateTempHome() =>
        Path.Combine(Path.GetTempPath(), "ro-stek-gateway-tests", Guid.NewGuid().ToString("N"));
}
