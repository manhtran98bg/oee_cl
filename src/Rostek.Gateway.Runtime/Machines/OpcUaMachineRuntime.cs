using System.Globalization;
using Opc.Ua;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.Machines;
using Rostek.Gateway.Contracts.Runtime;
using Rostek.Gateway.Runtime.OpcUa;

namespace Rostek.Gateway.Runtime.Machines;

public sealed class OpcUaMachineRuntime(
    EffectiveMachineConfiguration configuration,
    IOpcUaClientAdapterFactory adapterFactory,
    OpcUaValueConverter valueConverter,
    MachineValueStore valueStore,
    IOptions<RuntimeOptions> options,
    ILogger<OpcUaMachineRuntime> logger) : IMachineRuntime
{
    private readonly object _sync = new();
    private EffectiveMachineConfiguration _configuration = configuration;
    private MachineRuntimeStatusDto _status = new(configuration.MachineCode, MachineRuntimeState.Stopped, "Created", DateTimeOffset.UtcNow, 0, 0);
    private CancellationTokenSource? _runtimeCts;
    private Task? _runtimeTask;

    public string MachineCode => GetConfiguration().MachineCode;

    public MachineRuntimeStatusDto Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_runtimeTask is { IsCompleted: false })
            {
                return Task.CompletedTask;
            }

            if (!_configuration.Enabled)
            {
                SetStatus(MachineRuntimeState.Disabled, "Machine disabled", incrementStart: false);
                return Task.CompletedTask;
            }

            _runtimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            SetStatus(MachineRuntimeState.Starting, "OPC UA runtime starting", incrementStart: true);
            _runtimeTask = Task.Run(() => RunAsync(_runtimeCts.Token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? runtimeTask;
        CancellationTokenSource? runtimeCts;
        lock (_sync)
        {
            runtimeTask = _runtimeTask;
            runtimeCts = _runtimeCts;
            SetStatus(MachineRuntimeState.Stopping, "OPC UA runtime stopping", incrementStart: false);
        }

        runtimeCts?.Cancel();
        if (runtimeTask is not null)
        {
            try
            {
                await runtimeTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (runtimeCts?.IsCancellationRequested == true)
            {
            }
        }

        runtimeCts?.Dispose();
        lock (_sync)
        {
            _runtimeTask = null;
            _runtimeCts = null;
            SetStatus(MachineRuntimeState.Stopped, "OPC UA runtime stopped", incrementStart: false, incrementStop: true);
        }

        valueStore.MarkOffline(MachineCode, "Runtime stopped.");
    }

    public Task ApplyConfigurationAsync(EffectiveMachineConfiguration configuration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _configuration = configuration;
            SetStatus(MachineRuntimeState.Connected, "OPC UA configuration applied", incrementStart: false);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var adapter = await ConnectAsync(cancellationToken);
            if (adapter is null)
            {
                consecutiveFailures++;
                await DelayBeforeReconnectAsync(consecutiveFailures, cancellationToken);
                continue;
            }

            consecutiveFailures = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                var current = GetConfiguration();
                if (!current.Enabled)
                {
                    SetStatus(MachineRuntimeState.Disabled, "Machine disabled", incrementStart: false);
                    valueStore.MarkOffline(current.MachineCode, "Machine disabled.");
                    await Task.Delay(GetPollingInterval(current), cancellationToken);
                    continue;
                }

                if (await PollAsync(adapter, current, cancellationToken))
                {
                    consecutiveFailures = 0;
                    await Task.Delay(GetPollingInterval(current), cancellationToken);
                    continue;
                }

                consecutiveFailures++;
                await DelayBeforeReconnectAsync(consecutiveFailures, cancellationToken);
                break;
            }
        }
    }

    private async Task<IOpcUaClientAdapter?> ConnectAsync(CancellationToken cancellationToken)
    {
        var current = GetConfiguration();
        try
        {
            EnsureSupportedConnection(current);
            SetStatus(MachineRuntimeState.Connecting, "Connecting OPC UA", incrementStart: false);
            var adapter = await adapterFactory.CreateAsync(current.Connection, cancellationToken);
            SetStatus(MachineRuntimeState.Connected, "OPC UA connected", incrementStart: false);
            return adapter;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to connect OPC UA machine {MachineCode}", current.MachineCode);
            SetStatus(MachineRuntimeState.Reconnecting, ex.Message, incrementStart: false);
            valueStore.MarkOffline(current.MachineCode, ex.Message);
            return null;
        }
    }

    private async Task<bool> PollAsync(IOpcUaClientAdapter adapter, EffectiveMachineConfiguration current, CancellationToken cancellationToken)
    {
        var values = new List<SignalValueDto>();
        var now = DateTimeOffset.UtcNow;
        var online = true;

        foreach (var signal in current.Signals.Where(signal => signal.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                values.Add(await ReadSignalAsync(adapter, current, signal, now, cancellationToken));
            }
            catch (TimeoutException ex)
            {
                online = false;
                values.Add(CreateErrorValue(signal, MachineReadQuality.Timeout, now, ex.Message));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                online = false;
                values.Add(CreateErrorValue(signal, MachineReadQuality.Timeout, now, $"OPC UA request timed out: {ex.Message}"));
            }
            catch (FormatException ex)
            {
                values.Add(CreateErrorValue(signal, MachineReadQuality.ConfigError, now, ex.Message));
            }
            catch (Exception ex) when (IsConfigurationException(ex))
            {
                values.Add(CreateErrorValue(signal, MachineReadQuality.ConfigError, now, ex.Message));
            }
            catch (Exception ex)
            {
                online = false;
                logger.LogWarning(ex, "Failed to read OPC UA signal {SignalCode} from machine {MachineCode}", signal.SignalCode, current.MachineCode);
                values.Add(CreateErrorValue(signal, MachineReadQuality.Bad, now, ex.Message));
            }
        }

        valueStore.ReplaceSnapshot(current.MachineCode, online, now, values);
        SetStatus(
            online ? MachineRuntimeState.Connected : MachineRuntimeState.Degraded,
            online ? "OPC UA read completed" : "One or more OPC UA signals failed",
            incrementStart: false);
        return online;
    }

    private async Task<SignalValueDto> ReadSignalAsync(
        IOpcUaClientAdapter adapter,
        EffectiveMachineConfiguration machine,
        EffectiveSignalConfiguration signal,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        var nodeId = ParseNodeId(signal);
        var maxAttempts = Math.Max(1, machine.Connection.RetryCount + 1);
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var rawValue = await ExecuteReadWithTimeoutAsync(
                    token => adapter.ReadNodeValueAsync(nodeId, token),
                    machine.Connection,
                    cancellationToken);
                var typedValue = valueConverter.ConvertValue(rawValue, signal.DataType);
                var value = ApplyScaling(typedValue, signal);
                return new SignalValueDto(signal.SignalCode, value, signal.DataType, MachineReadQuality.Good, timestamp, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                lastException = new TimeoutException($"OPC UA request timed out after {machine.Connection.RequestTimeoutMs} ms.", ex);
            }
            catch (TimeoutException ex)
            {
                lastException = ex;
            }
            catch (Exception ex) when (IsConfigurationException(ex))
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            if (attempt < maxAttempts)
            {
                logger.LogWarning(
                    lastException,
                    "Retrying OPC UA read for machine {MachineCode}, signal {SignalCode}. Attempt={Attempt}, MaxAttempts={MaxAttempts}, ErrorType={ErrorType}",
                    machine.MachineCode,
                    signal.SignalCode,
                    attempt + 1,
                    maxAttempts,
                    lastException.GetType().Name);
            }
        }

        throw lastException ?? new InvalidOperationException("OPC UA read failed.");
    }

    private static async Task<T> ExecuteReadWithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> read,
        EffectiveConnectionConfiguration connection,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(Math.Max(1, connection.RequestTimeoutMs));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            return await read(linked.Token).WaitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"OPC UA request timed out after {connection.RequestTimeoutMs} ms.");
        }
    }

    private static NodeId ParseNodeId(EffectiveSignalConfiguration signal)
    {
        try
        {
            return NodeId.Parse(signal.SourceAddress);
        }
        catch (Exception ex)
        {
            throw new FormatException($"OPC UA signal '{signal.SignalCode}' source address must be a valid NodeId.", ex);
        }
    }

    private static object? ApplyScaling(object? rawValue, EffectiveSignalConfiguration signal)
    {
        if (rawValue is null ||
            rawValue is bool ||
            rawValue is string ||
            (signal.ScalingFactor == 1 && signal.ScalingOffset == 0))
        {
            return rawValue;
        }

        var numeric = Convert.ToDouble(rawValue, CultureInfo.InvariantCulture);
        return numeric * signal.ScalingFactor + signal.ScalingOffset;
    }

    private static void EnsureSupportedConnection(EffectiveMachineConfiguration machine)
    {
        var securityMode = machine.Connection.SecurityMode ?? "NONE";
        var securityPolicy = machine.Connection.SecurityPolicy ?? "None";
        var authenticationMode = machine.Connection.AuthenticationMode ?? "ANONYMOUS";

        if (!securityMode.Equals("NONE", StringComparison.OrdinalIgnoreCase) ||
            !securityPolicy.Equals("None", StringComparison.OrdinalIgnoreCase) ||
            !authenticationMode.Equals("ANONYMOUS", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("OPC UA V1 only supports anonymous connection with SecurityMode NONE and SecurityPolicy None.");
        }
    }

    private static bool IsConfigurationException(Exception exception) =>
        exception is FormatException or NotSupportedException or InvalidCastException;

    private static SignalValueDto CreateErrorValue(EffectiveSignalConfiguration signal, MachineReadQuality quality, DateTimeOffset timestamp, string error) =>
        new(signal.SignalCode, null, signal.DataType, quality, timestamp, error);

    private EffectiveMachineConfiguration GetConfiguration()
    {
        lock (_sync)
        {
            return _configuration;
        }
    }

    private void SetStatus(MachineRuntimeState state, string? message, bool incrementStart, bool incrementStop = false)
    {
        lock (_sync)
        {
            _status = _status with
            {
                State = state,
                Message = message,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                StartCount = incrementStart ? _status.StartCount + 1 : _status.StartCount,
                StopCount = incrementStop ? _status.StopCount + 1 : _status.StopCount
            };
        }
    }

    private TimeSpan GetPollingInterval(EffectiveMachineConfiguration current) =>
        TimeSpan.FromMilliseconds(Math.Max(
            options.Value.MinimumPollingIntervalMs,
            current.Connection.PollingIntervalMs ?? options.Value.MinimumPollingIntervalMs));

    public static TimeSpan CalculateReconnectBackoff(TimeSpan pollingInterval, int consecutiveFailures, int maxReconnectBackoffMs) =>
        ModbusTcpMachineRuntime.CalculateReconnectBackoff(pollingInterval, consecutiveFailures, maxReconnectBackoffMs);

    private async Task DelayBeforeReconnectAsync(int consecutiveFailures, CancellationToken cancellationToken)
    {
        var current = GetConfiguration();
        var delay = CalculateReconnectBackoff(GetPollingInterval(current), consecutiveFailures, options.Value.MaxReconnectBackoffMs);
        logger.LogWarning(
            "Waiting {DelayMs} ms before reconnecting OPC UA machine {MachineCode}. ConsecutiveFailures={ConsecutiveFailures}",
            (int)delay.TotalMilliseconds,
            current.MachineCode,
            consecutiveFailures);
        await Task.Delay(delay, cancellationToken);
    }
}
