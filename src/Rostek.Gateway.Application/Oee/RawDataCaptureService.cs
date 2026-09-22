using System.Globalization;
using Microsoft.Extensions.Logging;
using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.Machines;
using Rostek.Gateway.Contracts.Runtime;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Oee;

public sealed class RawDataCaptureService(
    IMachineValueReader valueReader,
    IRuntimeConfigurationProvider runtimeConfigurationProvider,
    IOeeLocalRepository repository,
    IProductionContextCache productionContextCache,
    IGatewayOeeTimerService gatewayTimerService,
    ILogger<RawDataCaptureService> logger) : IRawDataCaptureService
{
    public async Task<IReadOnlyList<PlcRawInterval>> CaptureAsync(
        TimeSpan interval,
        bool requireProductionContext,
        CancellationToken cancellationToken)
    {
        var intervalSeconds = Math.Max(1, (long)interval.TotalSeconds);
        var rawIntervals = new List<PlcRawInterval>();

        foreach (var snapshot in valueReader.GetSnapshots().OrderBy(item => item.MachineCode, StringComparer.OrdinalIgnoreCase))
        {
            var raw = await TryCreateRawIntervalAsync(snapshot, intervalSeconds, requireProductionContext, cancellationToken);
            if (raw is not null)
            {
                rawIntervals.Add(raw);
            }
        }

        if (rawIntervals.Count == 0)
        {
            logger.LogDebug("No online machine snapshots available for OEE raw interval capture");
            return [];
        }

        var inserted = await repository.InsertMissingRawIntervalsAsync(rawIntervals, cancellationToken);
        logger.LogDebug("Captured {InsertedCount} OEE raw intervals", inserted.Count);
        return inserted;
    }

    private async Task<PlcRawInterval?> TryCreateRawIntervalAsync(
        MachineValueSnapshotDto snapshot,
        long intervalSeconds,
        bool requireProductionContext,
        CancellationToken cancellationToken)
    {
        if (!snapshot.Online || snapshot.LastReadUtc is null || snapshot.Values.Count == 0)
        {
            return null;
        }

        var readAt = AlignToInterval(snapshot.LastReadUtc.Value.ToUniversalTime().ToUnixTimeSeconds(), intervalSeconds);
        var contexts = productionContextCache.GetCapturableByMachine(snapshot.MachineCode);
        if (contexts.Count == 0)
        {
            contexts = await repository.ListCapturableProductionContextsAsync(snapshot.MachineCode, cancellationToken);
        }

        if (contexts.Count == 0 && !requireProductionContext)
        {
            var context = await repository.EnsureTestProductionContextAsync(snapshot.MachineCode, readAt, cancellationToken);
            productionContextCache.Upsert(context);
            contexts = [context];
        }

        var primaryContext = contexts
            .OrderBy(context => context.OrderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(context => context.SessionId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (primaryContext is not null)
        {
            await repository.EnsureProductionPeriodAsync(
                primaryContext,
                primaryContext.ActivePeriodStartAt > 0 ? primaryContext.ActivePeriodStartAt : readAt,
                cancellationToken);
        }

        var signals = snapshot.Values
            .GroupBy(value => value.SignalCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(value => value.TimestampUtc).First(),
                StringComparer.OrdinalIgnoreCase);

        var runState = ReadRunState(signals);
        var deviceRunTime = ReadInt64(signals, OeeSignalCodes.RunTimeTotal);
        var deviceStopTime = ReadInt64(signals, OeeSignalCodes.StopTimeTotal);
        var deviceErrorTime = ReadInt64(signals, OeeSignalCodes.ErrorTimeTotal);
        var timeSource = GetTimeSource(snapshot.MachineCode);
        var useGatewayTimer = timeSource == OeeTimeSources.GatewayState ||
                              timeSource == OeeTimeSources.Auto &&
                              (deviceRunTime is null || deviceStopTime is null || deviceErrorTime is null);

        GatewayOeeTimerTotals? gatewayTotals = null;
        if (useGatewayTimer)
        {
            if (timeSource == OeeTimeSources.Auto)
            {
                gatewayTimerService.LogAutoFallbackOnce(snapshot.MachineCode);
            }

            gatewayTotals = await gatewayTimerService.CalculateAsync(
                snapshot.MachineCode,
                runState,
                readAt,
                intervalSeconds * 3,
                token => repository.GetLatestRawIntervalAsync(snapshot.MachineCode, token),
                cancellationToken);
        }
        else
        {
            gatewayTimerService.Reset(snapshot.MachineCode);
        }

        return new PlcRawInterval
        {
            Machine = snapshot.MachineCode,
            ReadAt = readAt,
            PlcPeriodIndex = primaryContext?.CurrentPlcPeriodIndex ?? 0,
            RunState = runState,
            ShotOkTotal = ReadInt64(signals, OeeSignalCodes.ShotOkCount) ?? 0,
            ShotNgTotal = ReadInt64(signals, OeeSignalCodes.ShotNgCount) ?? 0,
            RunTimeTotalSec = gatewayTotals?.RunTimeTotalSec ?? deviceRunTime ?? 0,
            StopTimeTotalSec = gatewayTotals?.StopTimeTotalSec ?? deviceStopTime ?? 0,
            ErrorTimeTotalSec = gatewayTotals?.ErrorTimeTotalSec ?? deviceErrorTime ?? 0,
            CycleTimeMs = ReadInt32(signals, OeeSignalCodes.CycleTimeMs) ?? 0,
            PeriodActive = primaryContext is null ? 0 : 1
        };
    }

    private string GetTimeSource(string machineCode)
    {
        if (!runtimeConfigurationProvider.Current.Machines.TryGetValue(machineCode, out var machine))
        {
            return OeeTimeSources.DeviceCounters;
        }

        var configured = OeeTimeSources.FromOptions(machine.Connection.Options);
        if (OeeTimeSources.IsValid(configured))
        {
            return configured;
        }

        logger.LogWarning(
            "Machine {MachineCode} has invalid OEE time source {OeeTimeSource}; device counters will be used",
            machineCode,
            configured);
        return OeeTimeSources.DeviceCounters;
    }

    private static string ReadRunState(IReadOnlyDictionary<string, SignalValueDto> signals)
    {
        if (!signals.TryGetValue(OeeSignalCodes.MachineState, out var signal) || signal.Value is null)
        {
            return OeeRunStates.Disconnect;
        }

        if (signal.Value is string text)
        {
            var normalized = text.Trim().ToLowerInvariant();
            return normalized switch
            {
                "1" or "run" or "running" or "production" => OeeRunStates.Run,
                "2" or "stop" or "stopped" or "pause" or "paused" => OeeRunStates.Stop,
                "3" or "error" or "fault" or "faulted" or "alarm" => OeeRunStates.Error,
                _ => OeeRunStates.Disconnect
            };
        }

        var numeric = ReadInt64(signals, OeeSignalCodes.MachineState);
        return numeric switch
        {
            0 => OeeRunStates.Stop,
            1 => OeeRunStates.Run,
            2 => OeeRunStates.Stop,
            3 => OeeRunStates.Error,
            _ => OeeRunStates.Disconnect
        };
    }

    private static long AlignToInterval(long unixTimeSeconds, long intervalSeconds) =>
        unixTimeSeconds - unixTimeSeconds % intervalSeconds;

    private static long? ReadInt64(IReadOnlyDictionary<string, SignalValueDto> signals, string signalCode)
    {
        if (!signals.TryGetValue(signalCode, out var signal) || signal.Value is null)
        {
            return null;
        }

        try
        {
            return signal.Value switch
            {
                bool boolean => boolean ? 1 : 0,
                string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => decimal.ToInt64(parsed),
                IConvertible convertible => Convert.ToInt64(convertible, CultureInfo.InvariantCulture),
                _ => null
            };
        }
        catch (Exception) when (signal.Value is IConvertible)
        {
            return null;
        }
    }

    private static int? ReadInt32(IReadOnlyDictionary<string, SignalValueDto> signals, string signalCode)
    {
        var value = ReadInt64(signals, signalCode);
        if (value is null || value < int.MinValue || value > int.MaxValue)
        {
            return null;
        }

        return (int)value.Value;
    }
}
