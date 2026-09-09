using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.UnitTests.Support;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class ProductionCommandServiceTests
{
    [Fact]
    public async Task Start_command_creates_active_python_context()
    {
        var repository = new InMemoryOeeLocalRepository();
        var cache = new ProductionContextCache();
        var service = CreateService(repository, cache);
        var occurredAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var response = await service.HandleAsync(new ProductionCommandRequest
        {
            MachineCode = "M16-01",
            MachineName = "Máy đúc M16-01",
            CommandCode = "CMD-001",
            Action = "start",
            OccurredAtUnixTimeSeconds = occurredAt,
            ProductionOrderCode = "MO-001",
            Products =
            [
                new ProductionCommandProduct
                {
                    ProductCode = "SP-001",
                    ProductName = "Vỏ nhựa A",
                    MoldCode = "KHUON-001",
                    Cavity = 4,
                    CycleTimeSeconds = 12.5m,
                    TargetQty = 1000
                }
            ]
        }, CancellationToken.None);

        Assert.True(response.Accepted);
        var context = Assert.Single(repository.Contexts);
        Assert.Equal("M16-01", context.Machine);
        Assert.Equal("active", context.Status);
        Assert.Equal("MO-001", context.OrderCode);
        Assert.Equal($"M16-01-MO-001-{occurredAt}", context.ActivePeriodId);
        Assert.Equal(occurredAt, context.ActivePeriodStartAt);
        Assert.Equal(occurredAt, context.UpdatedAt);
        Assert.Equal(0, context.BaselineCapturedAt);
        Assert.Equal(context.ActivePeriodId, response.SessionId);
        using var extraJson = JsonDocument.Parse(context.ExtraJson);
        Assert.Equal("Máy đúc M16-01", extraJson.RootElement.GetProperty("machine_name").GetString());
        Assert.Contains("KHUON-001", context.ProductsJson);
        using var productsJson = JsonDocument.Parse(context.ProductsJson);
        Assert.Equal("SP-001", productsJson.RootElement[0].GetProperty("product_id").GetString());
        Assert.Equal(4m, productsJson.RootElement[0].GetProperty("gain").GetDecimal());
        Assert.Equal(12.5m, productsJson.RootElement[0].GetProperty("cycle_time").GetDecimal());
        Assert.NotNull(cache.Get("M16-01"));
    }

    [Fact]
    public async Task Start_command_requires_products()
    {
        var service = CreateService();

        var response = await service.HandleAsync(new ProductionCommandRequest
        {
            MachineCode = "M16-01",
            CommandCode = "CMD-001",
            Action = "start",
            ProductionOrderCode = "MO-001"
        }, CancellationToken.None);

        Assert.False(response.Accepted);
        Assert.Contains("products", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_machine_is_rejected()
    {
        var service = new ProductionCommandService(
            new InMemoryOeeLocalRepository(),
            new ProductionContextCache(),
            new InMemoryConfigRepository("M16-01"),
            NullLogger<ProductionCommandService>.Instance);

        var response = await service.HandleAsync(new ProductionCommandRequest
        {
            MachineCode = "M16-99",
            CommandCode = "CMD-001",
            Action = "start",
            ProductionOrderCode = "MO-001",
            Products = [ValidProduct()]
        }, CancellationToken.None);

        Assert.False(response.Accepted);
        Assert.Contains("not found", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pause_command_updates_context_without_creating_period()
    {
        var repository = new InMemoryOeeLocalRepository();
        var cache = new ProductionContextCache();
        var service = CreateService(repository, cache);
        var startAt = 100;
        await service.HandleAsync(StartRequest(startAt), CancellationToken.None);

        var response = await service.HandleAsync(new ProductionCommandRequest
        {
            MachineCode = "M16-01",
            CommandCode = "CMD-002",
            Action = "pause",
            OccurredAtUnixTimeSeconds = 120
        }, CancellationToken.None);

        Assert.True(response.Accepted);
        Assert.Equal("pause", response.Status);
        Assert.Single(repository.Periods);
        Assert.Equal("pause", Assert.Single(repository.Contexts).Status);
    }

    [Fact]
    public async Task Stop_command_updates_context_and_closes_period()
    {
        var repository = new InMemoryOeeLocalRepository();
        var cache = new ProductionContextCache();
        var service = CreateService(repository, cache);
        await service.HandleAsync(StartRequest(100), CancellationToken.None);

        var response = await service.HandleAsync(new ProductionCommandRequest
        {
            MachineCode = "M16-01",
            CommandCode = "CMD-003",
            Action = "stop",
            OccurredAtUnixTimeSeconds = 150
        }, CancellationToken.None);

        Assert.True(response.Accepted);
        Assert.Equal("stopped", response.Status);
        Assert.Equal("stopped", Assert.Single(repository.Contexts).Status);
        Assert.Equal(150, Assert.Single(repository.Periods).EndAt);
        Assert.Equal("stopped", Assert.Single(repository.Periods).Status);
    }

    [Fact]
    public async Task Invalid_action_is_rejected()
    {
        var service = CreateService();

        var response = await service.HandleAsync(new ProductionCommandRequest
        {
            MachineCode = "M16-01",
            CommandCode = "CMD-001",
            Action = "bad"
        }, CancellationToken.None);

        Assert.False(response.Accepted);
        Assert.Contains("action", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ProductionCommandService CreateService(
        InMemoryOeeLocalRepository? repository = null,
        ProductionContextCache? cache = null) =>
        new(
            repository ?? new InMemoryOeeLocalRepository(),
            cache ?? new ProductionContextCache(),
            new InMemoryConfigRepository("M16-01"),
            NullLogger<ProductionCommandService>.Instance);

    private static ProductionCommandRequest StartRequest(long occurredAt) =>
        new()
        {
            MachineCode = "M16-01",
            CommandCode = "CMD-001",
            Action = "start",
            OccurredAtUnixTimeSeconds = occurredAt,
            ProductionOrderCode = "MO-001",
            Products = [ValidProduct()]
        };

    private static ProductionCommandProduct ValidProduct() =>
        new()
        {
            ProductCode = "SP-001",
            Cavity = 1,
            CycleTimeSeconds = 1
        };
}
