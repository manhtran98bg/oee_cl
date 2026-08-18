using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.Machines;
using Rostek.Gateway.Contracts.Runtime;
using Rostek.Gateway.Runtime.Machines;
using Rostek.Gateway.Runtime.Modbus;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class ModbusTcpMachineRuntimeTests
{
    [Fact]
    public async Task Runtime_reads_values_into_ram_snapshot()
    {
        var valueStore = new MachineValueStore();
        var adapter = new ScriptedModbusTcpClientAdapter([ReadOutcome.Success(123)]);
        var runtime = new ModbusTcpMachineRuntime(
            CreateMachine(retryCount: 0),
            new FakeModbusTcpClientAdapterFactory(adapter),
            new ModbusSignalAddressParser(),
            new ModbusValueDecoder(),
            valueStore,
            Options.Create(new RuntimeOptions { MinimumPollingIntervalMs = 10, MaxReconnectBackoffMs = 1000 }),
            NullLogger<ModbusTcpMachineRuntime>.Instance);

        await runtime.StartAsync(CancellationToken.None);
        var snapshot = await WaitForSnapshotAsync(valueStore, "M16-01");
        await runtime.StopAsync(CancellationToken.None);

        Assert.True(snapshot.Online);
        var speed = Assert.Single(snapshot.Values, value => value.SignalCode == "speed");
        Assert.Equal(MachineReadQuality.Good, speed.Quality);
        Assert.Equal((short)123, speed.Value);
    }

    [Fact]
    public async Task Runtime_retries_timed_out_read_and_keeps_machine_online_when_retry_succeeds()
    {
        var valueStore = new MachineValueStore();
        var adapter = new ScriptedModbusTcpClientAdapter([ReadOutcome.Timeout(), ReadOutcome.Success(456)]);
        var factory = new FakeModbusTcpClientAdapterFactory(adapter);
        var runtime = CreateRuntime(CreateMachine(retryCount: 1, requestTimeoutMs: 20), factory, valueStore);

        await runtime.StartAsync(CancellationToken.None);
        var snapshot = await WaitForSnapshotAsync(valueStore, "M16-01", value => value.Quality == MachineReadQuality.Good);
        await runtime.StopAsync(CancellationToken.None);

        Assert.True(snapshot.Online);
        Assert.Equal(2, adapter.HoldingRegisterReadCount);
        Assert.Equal(1, factory.CreateCount);
        Assert.Equal((short)456, Assert.Single(snapshot.Values).Value);
    }

    [Fact]
    public async Task Runtime_marks_signal_timeout_after_retry_budget_is_exhausted()
    {
        var valueStore = new MachineValueStore();
        var adapter = new ScriptedModbusTcpClientAdapter([ReadOutcome.Timeout(), ReadOutcome.Timeout()]);
        var runtime = CreateRuntime(CreateMachine(retryCount: 1, requestTimeoutMs: 20), new FakeModbusTcpClientAdapterFactory(adapter), valueStore);

        await runtime.StartAsync(CancellationToken.None);
        var snapshot = await WaitForSnapshotAsync(valueStore, "M16-01", value => value.Quality == MachineReadQuality.Timeout);
        await runtime.StopAsync(CancellationToken.None);

        Assert.False(snapshot.Online);
        Assert.Equal(2, adapter.HoldingRegisterReadCount);
        Assert.Equal(MachineReadQuality.Timeout, Assert.Single(snapshot.Values).Quality);
    }

    [Fact]
    public async Task Runtime_marks_signal_bad_after_non_timeout_retry_budget_is_exhausted()
    {
        var valueStore = new MachineValueStore();
        var adapter = new ScriptedModbusTcpClientAdapter([ReadOutcome.Failure(), ReadOutcome.Failure()]);
        var runtime = CreateRuntime(CreateMachine(retryCount: 1), new FakeModbusTcpClientAdapterFactory(adapter), valueStore);

        await runtime.StartAsync(CancellationToken.None);
        var snapshot = await WaitForSnapshotAsync(valueStore, "M16-01", value => value.Quality == MachineReadQuality.Bad);
        await runtime.StopAsync(CancellationToken.None);

        Assert.False(snapshot.Online);
        Assert.Equal(2, adapter.HoldingRegisterReadCount);
        Assert.Equal(MachineReadQuality.Bad, Assert.Single(snapshot.Values).Quality);
    }

    [Fact]
    public async Task Retry_count_zero_reads_only_once()
    {
        var valueStore = new MachineValueStore();
        var adapter = new ScriptedModbusTcpClientAdapter([ReadOutcome.Failure()]);
        var runtime = CreateRuntime(CreateMachine(retryCount: 0), new FakeModbusTcpClientAdapterFactory(adapter), valueStore);

        await runtime.StartAsync(CancellationToken.None);
        await WaitForSnapshotAsync(valueStore, "M16-01", value => value.Quality == MachineReadQuality.Bad);
        await runtime.StopAsync(CancellationToken.None);

        Assert.Equal(1, adapter.HoldingRegisterReadCount);
    }

    [Fact]
    public void Reconnect_backoff_grows_exponentially_and_is_capped()
    {
        var polling = TimeSpan.FromMilliseconds(100);

        Assert.Equal(TimeSpan.FromMilliseconds(100), ModbusTcpMachineRuntime.CalculateReconnectBackoff(polling, 1, 1000));
        Assert.Equal(TimeSpan.FromMilliseconds(200), ModbusTcpMachineRuntime.CalculateReconnectBackoff(polling, 2, 1000));
        Assert.Equal(TimeSpan.FromMilliseconds(400), ModbusTcpMachineRuntime.CalculateReconnectBackoff(polling, 3, 1000));
        Assert.Equal(TimeSpan.FromMilliseconds(1000), ModbusTcpMachineRuntime.CalculateReconnectBackoff(polling, 10, 1000));
    }

    private static ModbusTcpMachineRuntime CreateRuntime(
        EffectiveMachineConfiguration configuration,
        FakeModbusTcpClientAdapterFactory factory,
        MachineValueStore valueStore) =>
        new(
            configuration,
            factory,
            new ModbusSignalAddressParser(),
            new ModbusValueDecoder(),
            valueStore,
            Options.Create(new RuntimeOptions { MinimumPollingIntervalMs = 10, MaxReconnectBackoffMs = 1000 }),
            NullLogger<ModbusTcpMachineRuntime>.Instance);

    private static async Task<MachineValueSnapshotDto> WaitForSnapshotAsync(
        MachineValueStore valueStore,
        string machineCode,
        Func<SignalValueDto, bool>? valuePredicate = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!timeout.IsCancellationRequested)
        {
            if (valueStore.GetSnapshot(machineCode) is { Values.Count: > 0 } snapshot)
            {
                if (valuePredicate is null || snapshot.Values.Any(valuePredicate))
                {
                    return snapshot;
                }
            }

            await Task.Delay(20, timeout.Token);
        }

        throw new TimeoutException("Runtime did not publish a value snapshot.");
    }

    private static EffectiveMachineConfiguration CreateMachine(int retryCount, int requestTimeoutMs = 1000) =>
        new(
            Guid.NewGuid(),
            "M16-01",
            "Machine 01",
            "MODBUS_TCP",
            true,
            new EffectiveConnectionConfiguration("MODBUS_TCP", "127.0.0.1", 502, null, 1, null, null, null, null, 1000, requestTimeoutMs, retryCount, 1000, null),
            [new EffectiveSignalConfiguration("speed", "HR:40001", "Int16", 10, 1, 0, true, true, null, null)]);

    private sealed class FakeModbusTcpClientAdapterFactory(IModbusTcpClientAdapter adapter) : IModbusTcpClientAdapterFactory
    {
        public int CreateCount { get; private set; }

        public Task<IModbusTcpClientAdapter> CreateAsync(EffectiveConnectionConfiguration connection, CancellationToken cancellationToken)
        {
            CreateCount++;
            return Task.FromResult(adapter);
        }
    }

    private sealed class ScriptedModbusTcpClientAdapter(IReadOnlyList<ReadOutcome> outcomes) : IModbusTcpClientAdapter
    {
        private int _nextOutcome;
        public int HoldingRegisterReadCount { get; private set; }

        public Task<bool[]> ReadCoilsAsync(byte unitId, ushort startAddress, ushort numberOfPoints, CancellationToken cancellationToken) =>
            Task.FromResult(new[] { true });

        public Task<bool[]> ReadDiscreteInputsAsync(byte unitId, ushort startAddress, ushort numberOfPoints, CancellationToken cancellationToken) =>
            Task.FromResult(new[] { true });

        public async Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, ushort startAddress, ushort numberOfPoints, CancellationToken cancellationToken)
        {
            HoldingRegisterReadCount++;
            var outcome = outcomes[Math.Min(_nextOutcome, outcomes.Count - 1)];
            _nextOutcome++;
            if (outcome.Kind == ReadOutcomeKind.Timeout)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (outcome.Kind == ReadOutcomeKind.Failure)
            {
                throw new InvalidOperationException("Scripted Modbus failure.");
            }

            return [outcome.Value];
        }

        public Task<ushort[]> ReadInputRegistersAsync(byte unitId, ushort startAddress, ushort numberOfPoints, CancellationToken cancellationToken) =>
            Task.FromResult(new ushort[] { 123 });

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record ReadOutcome(ReadOutcomeKind Kind, ushort Value)
    {
        public static ReadOutcome Success(ushort value) => new(ReadOutcomeKind.Success, value);
        public static ReadOutcome Timeout() => new(ReadOutcomeKind.Timeout, 0);
        public static ReadOutcome Failure() => new(ReadOutcomeKind.Failure, 0);
    }

    private enum ReadOutcomeKind
    {
        Success,
        Timeout,
        Failure
    }
}
