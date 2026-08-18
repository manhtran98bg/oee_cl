using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Rostek.Gateway.IntegrationTests;

public sealed class DashboardEndpointTests
{
    [Fact]
    public async Task Dashboard_endpoint_returns_summary_and_machines()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    var dataDirectory = Path.Combine(Path.GetTempPath(), "ro-stek-gateway-tests", Guid.NewGuid().ToString("N"));
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Gateway:DataDirectory"] = dataDirectory,
                        ["Gateway:BackupDirectory"] = Path.Combine(dataDirectory, "backups"),
                        ["Gateway:ExportDirectory"] = Path.Combine(dataDirectory, "exports")
                    });
                });
            });

        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/dashboard");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"summary\"", json);
        Assert.Contains("\"machines\"", json);
        Assert.Contains("\"signals\"", json);
    }
}
