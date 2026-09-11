using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Oee;

public sealed class GatewayOeeTimerService(ILogger<GatewayOeeTimerService> logger) : IGatewayOeeTimerService
{
    private readonly ConcurrentDictionary<string, TimerState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _autoFallbackLogged = new(StringComparer.OrdinalIgnoreCase);

    public async Task<GatewayOeeTimerTotals> CalculateAsync(
        string machineCode,
        string runState,
        long readAt,
        long maxElapsedSeconds,
        Func<CancellationToken, Task<PlcRawInterval?>> loadLatestRawAsync,
        CancellationToken cancellationToken)
    {
        if (!_states.TryGetValue(machineCode, out var current))
        {
            var latestRaw = await loadLatestRawAsync(cancellationToken);
            var seed = new TimerState(
                readAt,
                latestRaw?.RunTimeTotalSec ?? 0,
                latestRaw?.StopTimeTotalSec ?? 0,
                latestRaw?.ErrorTimeTotalSec ?? 0);

            if (_states.TryAdd(machineCode, seed))
            {
                logger.LogInformation(
                    "Initialized Gateway OEE timer for machine {MachineCode}. PreviousRawFound={PreviousRawFound}, RunTimeTotalSec={RunTimeTotalSec}, StopTimeTotalSec={StopTimeTotalSec}, ErrorTimeTotalSec={ErrorTimeTotalSec}",
                    machineCode,
                    latestRaw is not null,
                    seed.RunTimeTotalSec,
                    seed.StopTimeTotalSec,
                    seed.ErrorTimeTotalSec);
                return seed.ToTotals();
            }

            current = _states[machineCode];
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (readAt <= current.ReadAt)
            {
                return current.ToTotals();
            }

            var elapsedSeconds = Math.Min(readAt - current.ReadAt, Math.Max(1, maxElapsedSeconds));
            var next = runState switch
            {
                OeeRunStates.Run => current with { ReadAt = readAt, RunTimeTotalSec = AddSaturated(current.RunTimeTotalSec, elapsedSeconds) },
                OeeRunStates.Stop => current with { ReadAt = readAt, StopTimeTotalSec = AddSaturated(current.StopTimeTotalSec, elapsedSeconds) },
                OeeRunStates.Error => current with { ReadAt = readAt, ErrorTimeTotalSec = AddSaturated(current.ErrorTimeTotalSec, elapsedSeconds) },
                _ => current with { ReadAt = readAt }
            };

            if (_states.TryUpdate(machineCode, next, current))
            {
                return next.ToTotals();
            }

            current = _states[machineCode];
        }
    }

    public void Reset(string machineCode)
    {
        _states.TryRemove(machineCode, out _);
        _autoFallbackLogged.TryRemove(machineCode, out _);
    }

    public void LogAutoFallbackOnce(string machineCode)
    {
        if (_autoFallbackLogged.TryAdd(machineCode, 0))
        {
            logger.LogInformation(
                "OEE time source auto selected Gateway state timer for machine {MachineCode} because one or more device timer signals are unavailable",
                machineCode);
        }
    }

    private static long AddSaturated(long value, long increment) =>
        value > long.MaxValue - increment ? long.MaxValue : value + increment;

    private sealed record TimerState(
        long ReadAt,
        long RunTimeTotalSec,
        long StopTimeTotalSec,
        long ErrorTimeTotalSec)
    {
        public GatewayOeeTimerTotals ToTotals() => new(RunTimeTotalSec, StopTimeTotalSec, ErrorTimeTotalSec);
    }
}
