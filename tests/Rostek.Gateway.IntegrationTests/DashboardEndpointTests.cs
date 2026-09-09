using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Domain.Enums;
using Rostek.Gateway.Infrastructure.Persistence;
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
            Assert.True(File.Exists(Path.Combine(Environment.GetEnvironmentVariable("ROSTEK_GATEWAY_HOME")!, "data", "config.db")));
            Assert.True(File.Exists(Path.Combine(Environment.GetEnvironmentVariable("ROSTEK_GATEWAY_HOME")!, "data", "oee.db")));
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
            await SeedMachineAsync(factory, "M16-01");

            var commandResponse = await client.PostAsJsonAsync(
                "/api/v1/mes/production-commands",
                new ProductionCommandRequest
                {
                    MachineCode = "M16-01",
                    MachineName = "Máy đúc M16-01",
                    CommandCode = "CMD-001",
                    Action = "start",
                    OccurredAtUnixTimeSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ProductionOrderCode = "MO-001",
                    Products =
                    [
                        new ProductionCommandProduct
                        {
                            ProductCode = "SP-001",
                            ProductName = "Vỏ nhựa A",
                            MoldCode = "KHUON-001",
                            Cavity = 4,
                            CycleTimeSeconds = 12.5m
                        }
                    ]
                });
            var statusResponse = await client.GetAsync("/api/v1/mes-sync/status");

            Assert.Equal(HttpStatusCode.OK, commandResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
            Assert.Contains("\"enabled\":false", await statusResponse.Content.ReadAsStringAsync());
            await AssertProductionContextSavedToOeeDbAsync(factory, "M16-01");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROSTEK_GATEWAY_HOME", previousGatewayHome);
        }
    }

    private static async Task SeedMachineAsync(WebApplicationFactory<Program> factory, string machineCode)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        var template = new MachineTemplate
        {
            Code = "TPL-MODBUS",
            Name = "Template Modbus",
            Protocol = GatewayProtocol.ModbusTcp
        };
        await dbContext.MachineTemplates.AddAsync(template);
        await dbContext.Machines.AddAsync(new Machine
        {
            Code = machineCode,
            Name = machineCode,
            TemplateId = template.Id
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task AssertProductionContextSavedToOeeDbAsync(WebApplicationFactory<Program> factory, string machineCode)
    {
        using var scope = factory.Services.CreateScope();
        var oeeDbContext = scope.ServiceProvider.GetRequiredService<OeeDbContext>();
        var context = await oeeDbContext.ProductionContexts.FindAsync(machineCode);
        Assert.NotNull(context);
        Assert.Equal("active", context.Status);
    }

    private static string CreateTempGatewayHome() =>
        Path.Combine(Path.GetTempPath(), "ro-stek-gateway-tests", Guid.NewGuid().ToString("N"), ".gateway");
}
