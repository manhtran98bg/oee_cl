using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Rostek.Gateway.Application.MesSync;
using Xunit;

namespace Rostek.Gateway.IntegrationTests;

public sealed class DashboardEndpointTests
{
    [Fact]
    public async Task Dashboard_endpoint_returns_summary_and_machines()
    {
        var previousGatewayHome = Environment.GetEnvironmentVariable("ROSTEK_GATEWAY_HOME");
        Environment.SetEnvironmentVariable("ROSTEK_GATEWAY_HOME", CreateTempGatewayHome());

        try
        {
            await using var factory = new WebApplicationFactory<Program>();
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/api/v1/dashboard");
            var json = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("\"summary\"", json);
            Assert.Contains("\"machines\"", json);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROSTEK_GATEWAY_HOME", previousGatewayHome);
        }
    }

    [Fact]
    public async Task Production_command_endpoint_updates_context_and_mes_sync_status_is_available()
    {
        var previousGatewayHome = Environment.GetEnvironmentVariable("ROSTEK_GATEWAY_HOME");
        Environment.SetEnvironmentVariable("ROSTEK_GATEWAY_HOME", CreateTempGatewayHome());

        try
        {
            await using var factory = new WebApplicationFactory<Program>();
            using var client = factory.CreateClient();

            var commandResponse = await client.PostAsJsonAsync(
                "/api/v1/mes/production-commands",
                new ProductionCommandRequest("M16-01", "CMD-001", "start", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "MO-001", "OP-01", null, null));
            var statusResponse = await client.GetAsync("/api/v1/mes-sync/status");

            Assert.Equal(HttpStatusCode.OK, commandResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
            Assert.Contains("\"enabled\":false", await statusResponse.Content.ReadAsStringAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROSTEK_GATEWAY_HOME", previousGatewayHome);
        }
    }

    private static string CreateTempGatewayHome() =>
        Path.Combine(Path.GetTempPath(), "ro-stek-gateway-tests", Guid.NewGuid().ToString("N"), ".gateway");
}
