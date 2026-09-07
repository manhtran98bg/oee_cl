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
        var service = new ProductionCommandService(repository, cache, NullLogger<ProductionCommandService>.Instance);
        var occurredAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var response = await service.HandleAsync(new ProductionCommandRequest("M16-01", "CMD-001", "start", occurredAt, "MO-001", "SESSION-001", "OP-01", null, "start run"), CancellationToken.None);

        Assert.True(response.Accepted);
        var context = Assert.Single(repository.Contexts);
        Assert.Equal("M16-01", context.Machine);
        Assert.Equal("active", context.Status);
        Assert.Equal("MO-001", context.OrderId);
        Assert.Equal("SESSION-001", context.ActivePeriodId);
        Assert.Equal(occurredAt, context.UpdatedAt);
        Assert.NotNull(cache.Get("M16-01"));
    }

    [Fact]
    public async Task Invalid_action_is_rejected()
    {
        var service = new ProductionCommandService(new InMemoryOeeLocalRepository(), new ProductionContextCache(), NullLogger<ProductionCommandService>.Instance);

        var response = await service.HandleAsync(new ProductionCommandRequest("M16-01", "CMD-001", "bad", null, null, null, null, null, null), CancellationToken.None);

        Assert.False(response.Accepted);
        Assert.Contains("action", response.Message, StringComparison.OrdinalIgnoreCase);
    }
}
