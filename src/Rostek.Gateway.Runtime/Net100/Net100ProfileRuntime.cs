using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.Machines;
using Rostek.Gateway.Contracts.Runtime;
using Rostek.Gateway.Runtime.Machines;

namespace Rostek.Gateway.Runtime.Net100;

public sealed class Net100ProfileRuntimeFactory(
    INet100ClientAdapterFactory adapterFactory,
    Net100LiveParser liveParser,
    MachineValueStore valueStore,
    IOptions<RuntimeOptions> options,
    ILogger<Net100ProfileRuntime> logger)
{
    public Net100ProfileRuntime Create(Guid profileId, IReadOnlyList<EffectiveMachineConfiguration> machines) =>
        new(profileId, machines, adapterFactory, liveParser, valueStore, options, logger);
}

public sealed class Net100ProfileRuntime : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly INet100ClientAdapter _adapter;
    private readonly Net100LiveParser _liveParser;
    private readonly MachineValueStore _valueStore;
    private readonly IOptions<RuntimeOptions> _options;
    private readonly ILogger<Net100ProfileRuntime> _logger;
    private readonly SemaphoreSlim _requestLimiter;
    private readonly ConcurrentDictionary<string, MachineRuntimeStatusDto> _statuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<long, Task> _pollTasks = [];
    private Dictionary<string, EffectiveMachineConfiguration> _machines;
    private Dictionary<string, ScheduledMachine> _schedules = new(StringComparer.OrdinalIgnoreCase);
    private long _generation;
    private long _nextTaskId;
    private CancellationTokenSource? _runtimeCts;
    private Task? _runtimeTask;

    public Net100ProfileRuntime(
        Guid profileId,
        IReadOnlyList<EffectiveMachineConfiguration> machines,
        INet100ClientAdapterFactory adapterFactory,
        Net100LiveParser liveParser,
        MachineValueStore valueStore,
        IOptions<RuntimeOptions> options,
        ILogger<Net100ProfileRuntime> logger)
    {
        if (machines.Count == 0)
        {
            throw new ArgumentException("NET100 profile requires at least one machine.", nameof(machines));
        }

        ProfileId = profileId;
        ServerEndpoint = machines[0].Connection.EndpointUrl
            ?? throw new InvalidOperationException("NET100 server endpoint is required.");
        _adapter = adapterFactory.Create(
            new Uri(ServerEndpoint, UriKind.Absolute),
            machines[0].Connection.AuthenticationMode,
            machines[0].Connection.CredentialReference);
        _liveParser = liveParser;
        _valueStore = valueStore;
        _options = options;
        _logger = logger;
        _requestLimiter = new SemaphoreSlim(Math.Max(1, options.Value.MaxNet100ConcurrentRequests));
        _machines = machines.ToDictionary(machine => machine.MachineCode, StringComparer.OrdinalIgnoreCase);

        foreach (var machine in machines)
        {
            _statuses[machine.MachineCode] = CreateStatus(machine.MachineCode, MachineRuntimeState.Stopped, "NET100 profile created", 0, 0);
        }
    }

    public Guid ProfileId { get; }
    public string ServerEndpoint { get; }

    public IReadOnlyCollection<MachineRuntimeStatusDto> GetStatuses() =>
        _statuses.Values.OrderBy(status => status.MachineCode, StringComparer.OrdinalIgnoreCase).ToList();

    public MachineRuntimeStatusDto? GetStatus(string machineCode) =>
        _statuses.TryGetValue(machineCode, out var status) ? status : null;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_runtimeTask is { IsCompleted: false })
            {
                return Task.CompletedTask;
            }

            _runtimeCts = new CancellationTokenSource();
            foreach (var machineCode in _machines.Keys)
            {
                UpdateStatus(machineCode, MachineRuntimeState.Starting, "NET100 shared profile starting", incrementStart: true);
            }

            RebuildSchedules(DateTimeOffset.UtcNow);

            _runtimeTask = Task.Run(() => RunAsync(_runtimeCts.Token), CancellationToken.None);
        }

        var currentMachines = GetMachineSnapshot();
        _logger.LogInformation(
            "Started NET100 shared profile scheduler {ProfileId}. Endpoint={Endpoint}, MachineCount={MachineCount}, MaxConcurrentRequests={MaxConcurrentRequests}, MinPollingIntervalMs={MinPollingIntervalMs}, MaxPollingIntervalMs={MaxPollingIntervalMs}",
            ProfileId,
            ServerEndpoint,
            currentMachines.Count,
            Math.Max(1, _options.Value.MaxNet100ConcurrentRequests),
            currentMachines.Count == 0 ? 0 : (int)currentMachines.Min(GetPollingInterval).TotalMilliseconds,
            currentMachines.Count == 0 ? 0 : (int)currentMachines.Max(GetPollingInterval).TotalMilliseconds);
        return Task.CompletedTask;
    }

    public Task ApplyConfigurationsAsync(IReadOnlyList<EffectiveMachineConfiguration> machines, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var next = machines.ToDictionary(machine => machine.MachineCode, StringComparer.OrdinalIgnoreCase);
        List<string> removed;
        List<string> added;
        lock (_sync)
        {
            removed = _machines.Keys.Except(next.Keys, StringComparer.OrdinalIgnoreCase).ToList();
            added = next.Keys.Except(_machines.Keys, StringComparer.OrdinalIgnoreCase).ToList();
            _machines = next;
            RebuildSchedules(DateTimeOffset.UtcNow);
        }

        foreach (var machineCode in removed)
        {
            if (_statuses.TryRemove(machineCode, out var oldStatus))
            {
                _logger.LogInformation("Removed machine {MachineCode} from NET100 profile {ProfileId}", machineCode, ProfileId);
                _valueStore.Remove(machineCode);
            }
        }

        foreach (var machineCode in added)
        {
            _statuses[machineCode] = CreateStatus(machineCode, MachineRuntimeState.Starting, "Added to NET100 shared profile", 1, 0);
            _logger.LogInformation("Added machine {MachineCode} to NET100 profile {ProfileId}", machineCode, ProfileId);
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
        }

        foreach (var machineCode in GetMachineSnapshot().Select(machine => machine.MachineCode))
        {
            UpdateStatus(machineCode, MachineRuntimeState.Stopped, "NET100 shared profile stopped", incrementStop: true);
            _valueStore.MarkOffline(machineCode, "NET100 shared profile stopped.");
        }

        _logger.LogInformation("Stopped NET100 shared profile {ProfileId}", ProfileId);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        await _adapter.DisposeAsync();
        _requestLimiter.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var due = TakeDueMachines(DateTimeOffset.UtcNow);
                foreach (var scheduled in due)
                {
                    var taskId = Interlocked.Increment(ref _nextTaskId);
                    var task = PollScheduledMachineAsync(scheduled, cancellationToken);
                    _pollTasks[taskId] = task;
                    _ = task.ContinueWith(
                        completedTask => _pollTasks.TryRemove(taskId, out _),
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }

                await Task.Delay(GetSchedulerDelay(DateTimeOffset.UtcNow), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        var activePolls = _pollTasks.Values.ToArray();
        if (activePolls.Length > 0)
        {
            try
            {
                await Task.WhenAll(activePolls);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private async Task PollScheduledMachineAsync(ScheduledPoll scheduled, CancellationToken cancellationToken)
    {
        try
        {
            await PollMachineAsync(scheduled.Machine, scheduled.Generation, cancellationToken);
        }
        finally
        {
            lock (_sync)
            {
                if (_schedules.TryGetValue(scheduled.Machine.MachineCode, out var current) &&
                    current.Generation == scheduled.Generation)
                {
                    current.InFlight = false;
                }
            }
        }
    }

    private async Task PollMachineAsync(
        EffectiveMachineConfiguration machine,
        long generation,
        CancellationToken cancellationToken)
    {
        var address = machine.Connection.Host;
        if (string.IsNullOrWhiteSpace(address))
        {
            UpdateStatus(machine.MachineCode, MachineRuntimeState.Faulted, "NET100 machine address is missing");
            _valueStore.MarkOffline(machine.MachineCode, "NET100 machine address is missing.");
            return;
        }

        Exception? lastError = null;
        var attempts = Math.Max(1, machine.Connection.RetryCount + 1);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                UpdateStatus(machine.MachineCode, MachineRuntimeState.Connecting, "Reading NET100 live data");
                string xml;
                await _requestLimiter.WaitAsync(cancellationToken);
                try
                {
                    xml = await _adapter.ReadLiveAsync(address, machine.Connection.RequestTimeoutMs, cancellationToken);
                }
                finally
                {
                    _requestLimiter.Release();
                }

                var live = _liveParser.Parse(xml);
                if (!IsCurrentMachine(machine.MachineCode, generation))
                {
                    return;
                }

                PublishSnapshot(machine, live);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastError = exception;
                if (attempt < attempts)
                {
                    _logger.LogWarning(
                        null,
                        "Retrying NET100 live request. Machine={MachineCode}, Address={MachineAddress}, Attempt={Attempt}, MaxAttempts={MaxAttempts}",
                        machine.MachineCode,
                        address,
                        attempt + 1,
                        attempts);
                    var retryDelayMs = Math.Min(
                        _options.Value.MaxReconnectBackoffMs,
                        500 * (1 << Math.Min(10, attempt - 1)));
                    await Task.Delay(Math.Max(100, retryDelayMs), cancellationToken);
                }
            }
        }

        if (!IsCurrentMachine(machine.MachineCode, generation))
        {
            return;
        }

        var message = lastError?.Message ?? "NET100 live request failed.";
        _logger.LogWarning(null, "NET100 live request failed for machine {MachineCode} at {MachineAddress}", machine.MachineCode, address);
        UpdateStatus(machine.MachineCode, MachineRuntimeState.Reconnecting, message);
        _valueStore.MarkOffline(machine.MachineCode, message);
    }

    private void PublishSnapshot(EffectiveMachineConfiguration machine, Net100LiveData live)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var values = new List<SignalValueDto>();
        foreach (var signal in machine.Signals.Where(signal => signal.Enabled))
        {
            try
            {
                var rawValue = ResolveValue(signal.SourceAddress, live);
                var converted = ConvertValue(rawValue, signal.DataType);
                var scaled = ApplyScaling(converted, signal);
                values.Add(new SignalValueDto(signal.SignalCode, scaled, signal.DataType, MachineReadQuality.Good, timestamp, null));
            }
            catch (NotSupportedException exception)
            {
                values.Add(new SignalValueDto(signal.SignalCode, null, signal.DataType, MachineReadQuality.ConfigError, timestamp, exception.Message));
            }
            catch (Exception exception)
            {
                values.Add(new SignalValueDto(signal.SignalCode, null, signal.DataType, MachineReadQuality.Bad, timestamp, exception.Message));
            }
        }

        _valueStore.ReplaceSnapshot(machine.MachineCode, true, timestamp, values);
        var hasBadSignal = values.Any(value => value.Quality != MachineReadQuality.Good);
        UpdateStatus(
            machine.MachineCode,
            hasBadSignal ? MachineRuntimeState.Degraded : MachineRuntimeState.Connected,
            hasBadSignal ? "NET100 live read completed with signal errors" : "NET100 live read completed");
    }

    private static object ResolveValue(string sourceAddress, Net100LiveData live) =>
        sourceAddress.Trim().ToLowerInvariant() switch
        {
            Net100Configuration.MachineStateSource => NormalizeMachineState(live.Status, live.Alarm),
            Net100Configuration.StatusSource => live.Status,
            Net100Configuration.AlarmSource => live.Alarm,
            Net100Configuration.ShotNumberSource => live.ShotNumber,
            Net100Configuration.CycleTimeMsSource when live.LastShotInfo is not null =>
                checked((int)Math.Round(live.LastShotInfo.CycleTimeSeconds * 1000, MidpointRounding.AwayFromZero)),
            Net100Configuration.QualityCodeSource when live.LastShotInfo is not null => live.LastShotInfo.QualityCode,
            Net100Configuration.CycleTimeMsSource or Net100Configuration.QualityCodeSource =>
                throw new FormatException("NET100 lastshotinfo is missing."),
            _ => throw new NotSupportedException($"NET100 source address '{sourceAddress}' is not supported.")
        };

    public static string NormalizeMachineState(string status, bool alarm)
    {
        if (alarm)
        {
            return "error";
        }

        return status.Trim().ToLowerInvariant() switch
        {
            "production" or "run" or "running" => "run",
            "stop" or "stopped" or "arrange" or "down" => "stop",
            "error" or "fault" or "faulted" or "alarm" => "error",
            _ => "disconnect"
        };
    }

    private static object ConvertValue(object value, string dataType) => dataType.Trim().ToUpperInvariant() switch
    {
        "BOOLEAN" => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
        "INT16" => Convert.ToInt16(value, CultureInfo.InvariantCulture),
        "INT32" => Convert.ToInt32(value, CultureInfo.InvariantCulture),
        "INT64" => Convert.ToInt64(value, CultureInfo.InvariantCulture),
        "FLOAT" => Convert.ToSingle(value, CultureInfo.InvariantCulture),
        "DOUBLE" => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        "STRING" => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => throw new NotSupportedException($"NET100 data type '{dataType}' is not supported.")
    };

    private static object ApplyScaling(object value, EffectiveSignalConfiguration signal)
    {
        if (value is bool or string || (signal.ScalingFactor == 1 && signal.ScalingOffset == 0))
        {
            return value;
        }

        return Convert.ToDouble(value, CultureInfo.InvariantCulture) * signal.ScalingFactor + signal.ScalingOffset;
    }

    private IReadOnlyList<EffectiveMachineConfiguration> GetMachineSnapshot()
    {
        lock (_sync)
        {
            return _machines.Values.OrderBy(machine => machine.MachineCode, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    private bool IsCurrentMachine(string machineCode, long generation)
    {
        lock (_sync)
        {
            return _machines.ContainsKey(machineCode) &&
                _schedules.TryGetValue(machineCode, out var scheduled) &&
                scheduled.Generation == generation;
        }
    }

    private void RebuildSchedules(DateTimeOffset now)
    {
        _generation++;
        var generation = _generation;
        var ordered = _machines.Values
            .OrderBy(machine => machine.MachineCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var schedules = new Dictionary<string, ScheduledMachine>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < ordered.Length; index++)
        {
            var machine = ordered[index];
            var interval = GetPollingInterval(machine);
            var offsetTicks = ordered.Length <= 1 ? 0 : interval.Ticks * index / ordered.Length;
            schedules[machine.MachineCode] = new ScheduledMachine(
                machine,
                generation,
                now + TimeSpan.FromTicks(offsetTicks));
        }

        _schedules = schedules;
    }

    private IReadOnlyList<ScheduledPoll> TakeDueMachines(DateTimeOffset now)
    {
        lock (_sync)
        {
            var due = new List<ScheduledPoll>();
            foreach (var scheduled in _schedules.Values.OrderBy(item => item.NextDueUtc))
            {
                if (scheduled.InFlight || scheduled.NextDueUtc > now)
                {
                    continue;
                }

                scheduled.InFlight = true;
                var interval = GetPollingInterval(scheduled.Machine);
                do
                {
                    scheduled.NextDueUtc += interval;
                }
                while (scheduled.NextDueUtc <= now);
                due.Add(new ScheduledPoll(scheduled.Machine, scheduled.Generation));
            }

            return due;
        }
    }

    private TimeSpan GetSchedulerDelay(DateTimeOffset now)
    {
        lock (_sync)
        {
            var next = _schedules.Values
                .Where(item => !item.InFlight)
                .Select(item => item.NextDueUtc)
                .DefaultIfEmpty(now.AddMilliseconds(250))
                .Min();
            var delay = next - now;
            return delay <= TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(10)
                : TimeSpan.FromMilliseconds(Math.Min(250, delay.TotalMilliseconds));
        }
    }

    private TimeSpan GetPollingInterval(EffectiveMachineConfiguration machine)
    {
        var configured = machine.Connection.PollingIntervalMs ?? _options.Value.MinimumPollingIntervalMs;
        return TimeSpan.FromMilliseconds(Math.Max(_options.Value.MinimumPollingIntervalMs, configured));
    }

    private sealed class ScheduledMachine(
        EffectiveMachineConfiguration machine,
        long generation,
        DateTimeOffset nextDueUtc)
    {
        public EffectiveMachineConfiguration Machine { get; } = machine;
        public long Generation { get; } = generation;
        public DateTimeOffset NextDueUtc { get; set; } = nextDueUtc;
        public bool InFlight { get; set; }
    }

    private sealed record ScheduledPoll(EffectiveMachineConfiguration Machine, long Generation);

    private void UpdateStatus(
        string machineCode,
        MachineRuntimeState state,
        string? message,
        bool incrementStart = false,
        bool incrementStop = false)
    {
        _statuses.AddOrUpdate(
            machineCode,
            _ => CreateStatus(machineCode, state, message, incrementStart ? 1 : 0, incrementStop ? 1 : 0),
            (_, current) => current with
            {
                State = state,
                Message = message,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                StartCount = incrementStart ? current.StartCount + 1 : current.StartCount,
                StopCount = incrementStop ? current.StopCount + 1 : current.StopCount
            });
    }

    private static MachineRuntimeStatusDto CreateStatus(
        string machineCode,
        MachineRuntimeState state,
        string? message,
        int startCount,
        int stopCount) =>
        new(machineCode, state, message, DateTimeOffset.UtcNow, startCount, stopCount);
}

public sealed class Net100RuntimeCoordinator(
    Net100ProfileRuntimeFactory profileFactory,
    ILogger<Net100RuntimeCoordinator> logger) : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _applyLock = new(1, 1);
    private readonly Dictionary<Guid, Net100ProfileRuntime> _profiles = [];

    public async Task ApplyConfigurationAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken)
    {
        var desired = configuration.Machines.Values
            .Where(machine => machine.Enabled && Net100Configuration.IsProtocol(machine.Protocol))
            .GroupBy(machine => machine.TemplateId == Guid.Empty ? machine.MachineId : machine.TemplateId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<EffectiveMachineConfiguration>)group.ToList());

        await _applyLock.WaitAsync(cancellationToken);
        try
        {
            List<Guid> existingIds;
            lock (_sync)
            {
                existingIds = _profiles.Keys.ToList();
            }

            foreach (var removedId in existingIds.Except(desired.Keys))
            {
                var removed = RemoveProfile(removedId);
                if (removed is not null)
                {
                    await removed.DisposeAsync();
                }
            }

            foreach (var (profileId, machines) in desired)
            {
                var endpoint = machines[0].Connection.EndpointUrl ?? string.Empty;
                var current = GetProfile(profileId);
                if (current is not null && current.ServerEndpoint.Equals(endpoint, StringComparison.OrdinalIgnoreCase))
                {
                    await current.ApplyConfigurationsAsync(machines, cancellationToken);
                    continue;
                }

                if (current is not null)
                {
                    RemoveProfile(profileId);
                    await current.DisposeAsync();
                }

                var profile = profileFactory.Create(profileId, machines);
                lock (_sync)
                {
                    _profiles[profileId] = profile;
                }

                await profile.StartAsync(cancellationToken);
            }
        }
        finally
        {
            _applyLock.Release();
        }
    }

    public IReadOnlyCollection<MachineRuntimeStatusDto> GetStatuses()
    {
        lock (_sync)
        {
            return _profiles.Values
                .SelectMany(profile => profile.GetStatuses())
                .OrderBy(status => status.MachineCode, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public MachineRuntimeStatusDto? GetStatus(string machineCode)
    {
        lock (_sync)
        {
            return _profiles.Values.Select(profile => profile.GetStatus(machineCode)).FirstOrDefault(status => status is not null);
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<Net100ProfileRuntime> profiles;
        lock (_sync)
        {
            profiles = _profiles.Values.ToList();
            _profiles.Clear();
        }

        foreach (var profile in profiles)
        {
            try
            {
                await profile.DisposeAsync();
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to dispose NET100 profile {ProfileId}", profile.ProfileId);
            }
        }

        _applyLock.Dispose();
    }

    private Net100ProfileRuntime? GetProfile(Guid profileId)
    {
        lock (_sync)
        {
            return _profiles.GetValueOrDefault(profileId);
        }
    }

    private Net100ProfileRuntime? RemoveProfile(Guid profileId)
    {
        lock (_sync)
        {
            return _profiles.Remove(profileId, out var profile) ? profile : null;
        }
    }
}
