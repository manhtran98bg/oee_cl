using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.UnitTests.Support;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class MachineStateEventBuilderTests
{
    [Fact]
    public async Task Raw_first_state_creates_open_event()
    {
        var repository = new InMemoryOeeLocalRepository();
        await repository.EnsureTestProductionContextAsync("M16-01", 100, CancellationToken.None);
        var builder = CreateBuilder(repository);

        var result = await builder.BuildAsync("GW-M16-01", [Raw(105, OeeRunStates.Run)], 105, CancellationToken.None);

        var stateEvent = Assert.Single(result.Events);
        Assert.Equal("run", stateEvent.State);
        Assert.True(stateEvent.IsOpen);
        Assert.Equal(105, stateEvent.StartAt);
        Assert.Equal(105, stateEvent.EndAt);
    }

    [Fact]
    public async Task Raw_same_state_updates_open_event()
    {
        var repository = new InMemoryOeeLocalRepository();
        await repository.EnsureTestProductionContextAsync("M16-01", 100, CancellationToken.None);
        var builder = CreateBuilder(repository);
        await builder.BuildAsync("GW-M16-01", [Raw(105, OeeRunStates.Run)], 105, CancellationToken.None);

        await builder.BuildAsync("GW-M16-01", [Raw(110, OeeRunStates.Run)], 110, CancellationToken.None);

        var stateEvent = Assert.Single(repository.MachineStateEvents);
        Assert.True(stateEvent.IsOpen);
        Assert.Equal(105, stateEvent.StartAt);
        Assert.Equal(110, stateEvent.EndAt);
        Assert.Equal(5, stateEvent.DurationSec);
    }

    [Fact]
    public async Task Raw_state_change_closes_old_event_and_creates_new_event()
    {
        var repository = new InMemoryOeeLocalRepository();
        await repository.EnsureTestProductionContextAsync("M16-01", 100, CancellationToken.None);
        var builder = CreateBuilder(repository);
        await builder.BuildAsync("GW-M16-01", [Raw(105, OeeRunStates.Run)], 105, CancellationToken.None);

        await builder.BuildAsync("GW-M16-01", [Raw(110, OeeRunStates.Stop)], 110, CancellationToken.None);

        Assert.Contains(repository.MachineStateEvents, item => item.State == OeeRunStates.Run && !item.IsOpen && item.EndAt == 110);
        Assert.Contains(repository.MachineStateEvents, item => item.State == OeeRunStates.Stop && item.IsOpen && item.StartAt == 110);
    }

    [Fact]
    public async Task Raw_gap_creates_disconnect_event()
    {
        var repository = new InMemoryOeeLocalRepository();
        await repository.EnsureTestProductionContextAsync("M16-01", 100, CancellationToken.None);
        var builder = CreateBuilder(repository, gapThresholdMs: 15000);
        await builder.BuildAsync("GW-M16-01", [Raw(105, OeeRunStates.Run)], 105, CancellationToken.None);

        await builder.BuildAsync("GW-M16-01", [Raw(200, OeeRunStates.Run)], 200, CancellationToken.None);

        Assert.Contains(repository.MachineStateEvents, item => item.State == OeeRunStates.Disconnect && item.StartAt == 105 && item.EndAt == 200);
        Assert.Contains(repository.MachineStateEvents, item => item.State == OeeRunStates.Run && item.IsOpen && item.StartAt == 200);
    }

    private static MachineStateEventBuilder CreateBuilder(InMemoryOeeLocalRepository repository, int gapThresholdMs = 15000)
    {
        var cache = new ProductionContextCache();
        cache.Replace(repository.ListProductionContextsAsync(CancellationToken.None).GetAwaiter().GetResult());
        return new MachineStateEventBuilder(
            repository,
            cache,
            Options.Create(new MesSyncOptions { MachineStateEventGapThresholdMs = gapThresholdMs }),
            NullLogger<MachineStateEventBuilder>.Instance);
    }

    private static PlcRawInterval Raw(long readAt, string state) =>
        new()
        {
            Machine = "M16-01",
            ReadAt = readAt,
            PlcPeriodIndex = 1,
            RunState = state,
            PeriodActive = 1
        };
}
