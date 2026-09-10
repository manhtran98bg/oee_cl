using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.Machines;
using Rostek.Gateway.Contracts.Runtime;
using Rostek.Gateway.Runtime.Machines;
using Rostek.Gateway.Runtime.Modbus;
using Rostek.Gateway.Runtime.OpcUa;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class OpcUaMachineRuntimeTests
{
    [Fact]
    public async Task Runtime_reads_values_into_ram_snapshot()
    {
        var valueStore = new MachineValueStore();
        var adapter = new ScriptedOpcUaClientAdapter([ReadOutcome.Success(42)]);
        var runtime = CreateRuntime(CreateMachine(retryCount: 0), new FakeOpcUaClientAdapterFactory(adapter), valueStore);

        await runtime.StartAsync(CancellationToken.None);
        var snapshot = await WaitForSnapshotAsync(valueStore, "OPCUA-TEST-01");
        await runtime.StopAsync(CancellationToken.None);

        Assert.True(snapshot.Online);
        var counter = Assert.Single(snapshot.Values, value => value.SignalCode == "COUNTER");
        Assert.Equal(MachineReadQuality.Good, counter.Quality);
        Assert.Equal(42, counter.Value);
        Assert.Equal("ns=1;s=cuulong.counter", adapter.LastNodeId?.ToString());
    }

    [Fact]
    public async Task Runtime_retries_timed_out_read_and_keeps_machine_online_when_retry_succeeds()
    {
        var valueStore = new MachineValueStore();
        var adapter = new ScriptedOpcUaClientAdapter([ReadOutcome.Timeout(), ReadOutcome.Success(43)]);
        var factory = new FakeOpcUaClientAdapterFactory(adapter);
        var runtime = CreateRuntime(CreateMachine(retryCount: 1, requestTimeoutMs: 20), factory, valueStore);

        await runtime.StartAsync(CancellationToken.None);
        var snapshot = await WaitForSnapshotAsync(valueStore, "OPCUA-TEST-01", value => value.Quality == MachineReadQuality.Good);
        await runtime.StopAsync(CancellationToken.None);

        Assert.True(snapshot.Online);
        Assert.Equal(2, adapter.ReadCount);
        Assert.Equal(1, factory.CreateCount);
        Assert.Equal(43, Assert.Single(snapshot.Values).Value);
    }

    [Fact]
    public async Task Runtime_marks_signal_timeout_after_retry_budget_is_exhausted()
    {
        var valueStore = new MachineValueStore();
        var adapter = new ScriptedOpcUaClientAdapter([ReadOutcome.Timeout(), ReadOutcome.Timeout()]);
        var runtime = CreateRuntime(CreateMachine(retryCount: 1, requestTimeoutMs: 20), new FakeOpcUaClientAdapterFactory(adapter), valueStore);

        await runtime.StartAsync(CancellationToken.None);
        var snapshot = await WaitForSnapshotAsync(valueStore, "OPCUA-TEST-01", value => value.Quality == MachineReadQuality.Timeout);
        await runtime.StopAsync(CancellationToken.None);

        Assert.False(snapshot.Online);
        Assert.Equal(2, adapter.ReadCount);
        Assert.Equal(MachineReadQuality.Timeout, Assert.Single(snapshot.Values).Quality);
    }

    [Fact]
    public async Task Retry_count_zero_reads_only_once()
    {
        var valueStore = new MachineValueStore();
        var adapter = new ScriptedOpcUaClientAdapter([ReadOutcome.Failure()]);
        var runtime = CreateRuntime(CreateMachine(retryCount: 0), new FakeOpcUaClientAdapterFactory(adapter), valueStore);

        await runtime.StartAsync(CancellationToken.None);
        await WaitForSnapshotAsync(valueStore, "OPCUA-TEST-01", value => value.Quality == MachineReadQuality.Bad);
        await runtime.StopAsync(CancellationToken.None);

        Assert.Equal(1, adapter.ReadCount);
    }

    [Fact]
    public void Protocol_factory_routes_opcua_to_opcua_runtime()
    {
        var valueStore = new MachineValueStore();
        var opcUaFactory = new OpcUaMachineRuntimeFactory(
            new FakeOpcUaClientAdapterFactory(new ScriptedOpcUaClientAdapter([ReadOutcome.Success(42)])),
            new OpcUaValueConverter(),
            valueStore,
            Options.Create(new RuntimeOptions { MinimumPollingIntervalMs = 10, MaxReconnectBackoffMs = 1000 }),
            NullLogger<OpcUaMachineRuntime>.Instance);
        var modbusFactory = new ModbusTcpMachineRuntimeFactory(
            new FakeModbusTcpClientAdapterFactory(),
            new ModbusSignalAddressParser(),
            new ModbusValueDecoder(),
            valueStore,
            Options.Create(new RuntimeOptions { MinimumPollingIntervalMs = 10, MaxReconnectBackoffMs = 1000 }),
            NullLogger<ModbusTcpMachineRuntime>.Instance);
        var protocolFactory = new ProtocolMachineRuntimeFactory(new UnsupportedProtocolMachineRuntimeFactory(), modbusFactory, opcUaFactory);

        var runtime = protocolFactory.Create(CreateMachine(retryCount: 0));

        Assert.IsType<OpcUaMachineRuntime>(runtime);
    }

    private static OpcUaMachineRuntime CreateRuntime(
        EffectiveMachineConfiguration configuration,
        FakeOpcUaClientAdapterFactory factory,
        MachineValueStore valueStore) =>
        new(
            configuration,
            factory,
            new OpcUaValueConverter(),
            valueStore,
            Options.Create(new RuntimeOptions { MinimumPollingIntervalMs = 10, MaxReconnectBackoffMs = 1000 }),
            NullLogger<OpcUaMachineRuntime>.Instance);

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
            "OPCUA-TEST-01",
            "CuuLong OPC UA Test",
            "OPCUA",
            true,
            new EffectiveConnectionConfiguration("OPCUA", null, null, "opc.tcp://192.168.1.10:4840", null, "NONE", "None", "ANONYMOUS", null, 1000, requestTimeoutMs, retryCount, 1000, null),
            [new EffectiveSignalConfiguration("COUNTER", "ns=1;s=cuulong.counter", "Int32", 10, 1, 0, true, true, null, null)]);

    private sealed class FakeOpcUaClientAdapterFactory(IOpcUaClientAdapter adapter) : IOpcUaClientAdapterFactory
    {
        public int CreateCount { get; private set; }

        public Task<IOpcUaClientAdapter> CreateAsync(EffectiveConnectionConfiguration connection, CancellationToken cancellationToken)
        {
            CreateCount++;
            return Task.FromResult(adapter);
        }
    }

    private sealed class ScriptedOpcUaClientAdapter(IReadOnlyList<ReadOutcome> outcomes) : IOpcUaClientAdapter
    {
        private int _nextOutcome;
        public int ReadCount { get; private set; }
        public NodeId? LastNodeId { get; private set; }

        public async Task<object?> ReadNodeValueAsync(NodeId nodeId, CancellationToken cancellationToken)
        {
            ReadCount++;
            LastNodeId = nodeId;
            var outcome = outcomes[Math.Min(_nextOutcome, outcomes.Count - 1)];
            _nextOutcome++;
            if (outcome.Kind == ReadOutcomeKind.Timeout)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (outcome.Kind == ReadOutcomeKind.Failure)
            {
                throw new InvalidOperationException("Scripted OPC UA failure.");
            }

            return outcome.Value;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeModbusTcpClientAdapterFactory : IModbusTcpClientAdapterFactory
    {
        public Task<IModbusTcpClientAdapter> CreateAsync(EffectiveConnectionConfiguration connection, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Modbus factory should not be used by this test.");
    }

    private sealed record ReadOutcome(ReadOutcomeKind Kind, object? Value)
    {
        public static ReadOutcome Success(object? value) => new(ReadOutcomeKind.Success, value);
        public static ReadOutcome Timeout() => new(ReadOutcomeKind.Timeout, null);
        public static ReadOutcome Failure() => new(ReadOutcomeKind.Failure, null);
    }

    private enum ReadOutcomeKind
    {
        Success,
        Timeout,
        Failure
    }
}
