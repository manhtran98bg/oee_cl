using System.Net.Sockets;
using NModbus;
using NModbus.Device;
using Rostek.Gateway.Contracts.Configuration;

namespace Rostek.Gateway.Runtime.Modbus;

public interface IModbusTcpClientAdapter : IAsyncDisposable
{
    Task<bool[]> ReadCoilsAsync(byte unitId, ushort startAddress, ushort numberOfPoints, CancellationToken cancellationToken);
    Task<bool[]> ReadDiscreteInputsAsync(byte unitId, ushort startAddress, ushort numberOfPoints, CancellationToken cancellationToken);
    Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, ushort startAddress, ushort numberOfPoints, CancellationToken cancellationToken);
    Task<ushort[]> ReadInputRegistersAsync(byte unitId, ushort startAddress, ushort numberOfPoints, CancellationToken cancellationToken);
}

public interface IModbusTcpClientAdapterFactory
{
    Task<IModbusTcpClientAdapter> CreateAsync(EffectiveConnectionConfiguration connection, CancellationToken cancellationToken);
}

public sealed class NModbusTcpClientAdapterFactory : IModbusTcpClientAdapterFactory
{
    public async Task<IModbusTcpClientAdapter> CreateAsync(EffectiveConnectionConfiguration connection, CancellationToken cancellationToken)
    {
        var host = string.IsNullOrWhiteSpace(connection.Host)
            ? throw new InvalidOperationException("Modbus host is required.")
            : connection.Host;
        var port = connection.Port ?? 502;
        var tcpClient = new TcpClient
        {
            ReceiveTimeout = connection.RequestTimeoutMs,
            SendTimeout = connection.RequestTimeoutMs
        };

        try
        {
            using var timeout = new CancellationTokenSource(connection.ConnectTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            await tcpClient.ConnectAsync(host, port, linked.Token);

            var master = new ModbusFactory().CreateMaster(tcpClient);
            return new NModbusTcpClientAdapter(tcpClient, master);
        }
        catch
        {
            tcpClient.Dispose();
            throw;
        }
    }
}

public sealed class NModbusTcpClientAdapter(TcpClient tcpClient, IModbusMaster master) : IModbusTcpClientAdapter
{
    public async Task<bool[]> ReadCoilsAsync(byte unitId, ushort startAddress, ushort numberOfPoints, CancellationToken cancellationToken) =>
        await master.ReadCoilsAsync(unitId, startAddress, numberOfPoints).WaitAsync(cancellationToken);

    public async Task<bool[]> ReadDiscreteInputsAsync(byte unitId, ushort startAddress, ushort numberOfPoints, CancellationToken cancellationToken) =>
        await master.ReadInputsAsync(unitId, startAddress, numberOfPoints).WaitAsync(cancellationToken);

    public async Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, ushort startAddress, ushort numberOfPoints, CancellationToken cancellationToken) =>
        await master.ReadHoldingRegistersAsync(unitId, startAddress, numberOfPoints).WaitAsync(cancellationToken);

    public async Task<ushort[]> ReadInputRegistersAsync(byte unitId, ushort startAddress, ushort numberOfPoints, CancellationToken cancellationToken) =>
        await master.ReadInputRegistersAsync(unitId, startAddress, numberOfPoints).WaitAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        master.Dispose();
        tcpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
