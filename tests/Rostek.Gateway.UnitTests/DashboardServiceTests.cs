using Rostek.Gateway.Application.Common;
using Rostek.Gateway.Application.Dashboard;
using Rostek.Gateway.Application.Machines;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.Machines;
using Rostek.Gateway.Contracts.Runtime;
using Rostek.Gateway.Domain.Enums;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class DashboardServiceTests
{
    [Fact]
    public async Task Connected_machine_with_online_snapshot_is_online()
    {
        var service = CreateService(
            [CreateMachine("M16-01", true)],
            [new MachineRuntimeStatusDto("M16-01", MachineRuntimeState.Connected, "Connected", DateTimeOffset.UtcNow, 1, 0)],
            [new MachineValueSnapshotDto("M16-01", true, DateTimeOffset.UtcNow, [])]);

        var dashboard = await service.GetAsync(CancellationToken.None);

        var row = Assert.Single(dashboard.Machines);
        Assert.True(row.Online);
        Assert.Equal("Connected", row.RuntimeState);
        Assert.Equal(1, dashboard.Summary.Online);
    }

    [Fact]
    public async Task Dashboard_row_includes_sorted_signal_values_and_counts()
    {
        var now = DateTimeOffset.UtcNow;
        var service = CreateService(
            [CreateMachine("M16-01", true)],
            [new MachineRuntimeStatusDto("M16-01", MachineRuntimeState.Connected, "Connected", now, 1, 0)],
            [
                new MachineValueSnapshotDto("M16-01", true, now,
                [
                    new SignalValueDto("temperature", 31.5, "Double", MachineReadQuality.Good, now, null),
                    new SignalValueDto("alarm", null, "Boolean", MachineReadQuality.Bad, now, "Read failed")
                ])
            ]);

        var dashboard = await service.GetAsync(CancellationToken.None);

        var row = Assert.Single(dashboard.Machines);
        Assert.Equal(2, row.SignalCount);
        Assert.Equal(1, row.GoodSignalCount);
        Assert.Equal(1, row.BadSignalCount);
        Assert.Collection(
            row.Signals,
            signal =>
            {
                Assert.Equal("alarm", signal.SignalCode);
                Assert.Equal("-", signal.Value);
                Assert.Equal("Bad", signal.Quality);
            },
            signal =>
            {
                Assert.Equal("temperature", signal.SignalCode);
                Assert.Equal("31.5", signal.Value);
                Assert.Equal("Good", signal.Quality);
            });
    }

    [Fact]
    public async Task Dashboard_replaces_unsupported_signal_value_with_placeholder()
    {
        var now = DateTimeOffset.UtcNow;
        var service = CreateService(
            [CreateMachine("M16-01", true)],
            [new MachineRuntimeStatusDto("M16-01", MachineRuntimeState.Connected, "Connected", now, 1, 0)],
            [new MachineValueSnapshotDto("M16-01", true, now, [new SignalValueDto("array", new[] { 1, 2 }, "Int32", MachineReadQuality.Good, now, null)])]);

        var dashboard = await service.GetAsync(CancellationToken.None);

        Assert.Equal("[unsupported]", Assert.Single(Assert.Single(dashboard.Machines).Signals).Value);
    }

    [Fact]
    public async Task Enabled_machine_without_runtime_is_not_started()
    {
        var service = CreateService([CreateMachine("M16-01", true)], [], []);

        var dashboard = await service.GetAsync(CancellationToken.None);

        var row = Assert.Single(dashboard.Machines);
        Assert.False(row.Online);
        Assert.Equal("NotStarted", row.RuntimeState);
        Assert.Equal("No runtime status", row.Message);
        Assert.Empty(row.Signals);
        Assert.Equal(0, row.SignalCount);
    }

    [Fact]
    public async Task Disabled_machine_is_marked_disabled()
    {
        var service = CreateService([CreateMachine("M16-01", false)], [], []);

        var dashboard = await service.GetAsync(CancellationToken.None);

        var row = Assert.Single(dashboard.Machines);
        Assert.False(row.Online);
        Assert.Equal("Disabled", row.RuntimeState);
        Assert.Equal(0, dashboard.Summary.Enabled);
    }

    private static DashboardService CreateService(
        IReadOnlyList<MachineListItem> machines,
        IReadOnlyCollection<MachineRuntimeStatusDto> statuses,
        IReadOnlyCollection<MachineValueSnapshotDto> snapshots) =>
        new(
            new FakeMachineService(machines),
            new FakeRuntimeConfigurationProvider(new RuntimeConfiguration(7, DateTimeOffset.UtcNow, new Dictionary<string, EffectiveMachineConfiguration>())),
            new FakeRuntimeStatusReader(statuses),
            new FakeMachineValueReader(snapshots));

    private static MachineListItem CreateMachine(string code, bool enabled) =>
        new(Guid.NewGuid(), code, $"Machine {code}", null, "JSW-MODBUS", GatewayProtocol.ModbusTcp, "127.0.0.1:502", enabled);

    private sealed class FakeMachineService(IReadOnlyList<MachineListItem> machines) : IMachineService
    {
        public Task<MachineListResult> ListAsync(MachineQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new MachineListResult(machines, query.Page, query.PageSize));

        public Task<MachineEditInput?> GetInputAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<MachineEditInput?>(null);

        public Task<GatewayResult<Guid>> SaveAsync(MachineEditInput input, string? userName, CancellationToken cancellationToken) =>
            Task.FromResult(GatewayResult<Guid>.Ok(Guid.NewGuid()));

        public Task<GatewayResult<Guid>> CloneAsync(Guid id, string newCode, string? userName, CancellationToken cancellationToken) =>
            Task.FromResult(GatewayResult<Guid>.Ok(Guid.NewGuid()));

        public Task<GatewayResult> SetEnabledAsync(Guid id, bool enabled, string? userName, CancellationToken cancellationToken) =>
            Task.FromResult(GatewayResult.Ok());

        public Task<RuntimeConfiguration?> PreviewEffectiveAsync(Guid machineId, CancellationToken cancellationToken) =>
            Task.FromResult<RuntimeConfiguration?>(null);
    }

    private sealed class FakeRuntimeConfigurationProvider(RuntimeConfiguration current) : IRuntimeConfigurationProvider
    {
        public RuntimeConfiguration Current { get; } = current;

        public Task ReplaceAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeRuntimeStatusReader(IReadOnlyCollection<MachineRuntimeStatusDto> statuses) : IRuntimeStatusReader
    {
        public IReadOnlyCollection<MachineRuntimeStatusDto> GetStatuses() => statuses;

        public MachineRuntimeStatusDto? GetStatus(string machineCode) =>
            statuses.FirstOrDefault(status => status.MachineCode.Equals(machineCode, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeMachineValueReader(IReadOnlyCollection<MachineValueSnapshotDto> snapshots) : IMachineValueReader
    {
        public IReadOnlyCollection<MachineValueSnapshotDto> GetSnapshots() => snapshots;

        public MachineValueSnapshotDto? GetSnapshot(string machineCode) =>
            snapshots.FirstOrDefault(snapshot => snapshot.MachineCode.Equals(machineCode, StringComparison.OrdinalIgnoreCase));
    }
}
