using Microsoft.Extensions.Logging.Abstractions;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Domain.Enums;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class ProductionCommandServiceTests
{
    [Fact]
    public async Task Start_command_creates_started_context()
    {
        var repository = new FakeMesSyncOutboxRepository();
        var service = new ProductionCommandService(repository, NullLogger<ProductionCommandService>.Instance);
        var occurredAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var response = await service.HandleAsync(new ProductionCommandRequest("M16-01", "CMD-001", "start", occurredAt, "MO-001", "SESSION-001", "OP-01", null, "start run"), CancellationToken.None);

        Assert.True(response.Accepted);
        var context = Assert.Single(repository.Contexts.Values);
        Assert.Equal("M16-01", context.MachineCode);
        Assert.Equal("CMD-001", context.CommandCode);
        Assert.Equal(ProductionContextStatus.Started, context.Status);
        Assert.Equal("MO-001", context.ProductionOrderCode);
        Assert.Equal("SESSION-001", context.SessionId);
        Assert.Equal(occurredAt, context.StartedUnixTimeSeconds);
    }

    [Fact]
    public async Task Invalid_action_is_rejected()
    {
        var service = new ProductionCommandService(new FakeMesSyncOutboxRepository(), NullLogger<ProductionCommandService>.Instance);

        var response = await service.HandleAsync(new ProductionCommandRequest("M16-01", "CMD-001", "bad", null, null, null, null, null, null), CancellationToken.None);

        Assert.False(response.Accepted);
        Assert.Contains("action", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeMesSyncOutboxRepository : IMesSyncOutboxRepository
    {
        public Dictionary<string, ProductionContext> Contexts { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<ProductionContext?> GetProductionContextAsync(string machineCode, CancellationToken cancellationToken) =>
            Task.FromResult(Contexts.GetValueOrDefault(machineCode));

        public Task<IReadOnlyDictionary<string, ProductionContext>> ListActiveProductionContextsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, ProductionContext>>(Contexts);

        public Task SaveProductionContextAsync(ProductionContext context, CancellationToken cancellationToken)
        {
            Contexts[context.MachineCode] = context;
            return Task.CompletedTask;
        }

        public Task AddOutboxMessageAsync(MesSyncOutboxMessage message, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<List<MesSyncOutboxMessage>> TakePendingAsync(long nowUnixTimeSeconds, int batchSize, CancellationToken cancellationToken) =>
            Task.FromResult(new List<MesSyncOutboxMessage>());

        public Task MarkSyncedAsync(long id, long nowUnixTimeSeconds, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkFailedAsync(long id, string error, long nextAttemptUnixTimeSeconds, long nowUnixTimeSeconds, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<MesSyncStatusDto> GetStatusAsync(bool enabled, CancellationToken cancellationToken) =>
            Task.FromResult(new MesSyncStatusDto(enabled, 0, 0, null, null));
    }
}
