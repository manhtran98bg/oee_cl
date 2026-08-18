using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Rostek.Gateway.Contracts.Machines;
using Rostek.Gateway.Contracts.Runtime;

namespace Rostek.Gateway.Application.History;

public sealed class DeviceHistorySampler(
    IMachineValueReader valueReader,
    IDeviceSampleWriter writer,
    ILogger<DeviceHistorySampler> logger) : IDeviceHistorySampler
{
    private const string MachineState = "MACHINE_STATE";
    private const string ShotOkCount = "SHOT_OK_COUNT";
    private const string ShotOkDelta = "SHOT_OK_DELTA";
    private const string ShotNgCount = "SHOT_NG_COUNT";
    private const string ShotNgDelta = "SHOT_NG_DELTA";
    private const string CycleTimeMs = "CYCLE_TIME_MS";
    private const string RunTimeTotal = "RUN_TIME_TOTAL";
    private const string RunTimeTotalMsAlias = "RUN_TIME_TOTAL_MS";
    private const string RunTimeDeltaMs = "RUN_TIME_DELTA_MS";
    private const string StopTimeTotal = "STOP_TIME_TOTAL";
    private const string StopTimeTotalMsAlias = "STOP_TIME_TOTAL_MS";
    private const string StopTimeDeltaMs = "STOP_TIME_DELTA_MS";
    private const string ErrorTimeTotal = "ERROR_TIME_TOTAL";
    private const string ErrorTimeTotalMsAlias = "ERROR_TIME_TOTAL_MS";
    private const string ErrorTimeDeltaMs = "ERROR_TIME_DELTA_MS";
    private readonly ConcurrentDictionary<string, RawDeviceSample> _baselines = new(StringComparer.OrdinalIgnoreCase);

    public async Task<int> SampleAndWriteAsync(string gatewayId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayId);

        var samples = new List<DeviceSample>();
        foreach (var current in valueReader.GetSnapshots().Select(TryCreateRawSample).OfType<RawDeviceSample>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_baselines.TryGetValue(current.MachineCode, out var previous))
            {
                _baselines[current.MachineCode] = current;
                continue;
            }

            samples.Add(CreateDeltaSample(gatewayId, previous, current));
            _baselines[current.MachineCode] = current;
        }

        if (samples.Count == 0)
        {
            logger.LogDebug("No device history delta samples available to write");
            return 0;
        }

        await writer.WriteAsync(samples, cancellationToken);
        logger.LogDebug("Wrote {SampleCount} device history samples", samples.Count);
        return samples.Count;
    }

    private static RawDeviceSample? TryCreateRawSample(MachineValueSnapshotDto snapshot)
    {
        if (!snapshot.Online || snapshot.LastReadUtc is null || snapshot.Values.Count == 0)
        {
            return null;
        }

        var signals = snapshot.Values
            .GroupBy(value => value.SignalCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(value => value.TimestampUtc).First(),
                StringComparer.OrdinalIgnoreCase);

        return new RawDeviceSample(
            snapshot.MachineCode,
            snapshot.LastReadUtc.Value,
            ReadInt32(signals, MachineState),
            ReadInt64(signals, ShotOkCount, ShotOkDelta),
            ReadInt64(signals, ShotNgCount, ShotNgDelta),
            ReadInt32(signals, CycleTimeMs),
            ReadInt64(signals, RunTimeTotal, RunTimeTotalMsAlias, RunTimeDeltaMs),
            ReadInt64(signals, StopTimeTotal, StopTimeTotalMsAlias, StopTimeDeltaMs),
            ReadInt64(signals, ErrorTimeTotal, ErrorTimeTotalMsAlias, ErrorTimeDeltaMs));
    }

    private static DeviceSample CreateDeltaSample(string gatewayId, RawDeviceSample previous, RawDeviceSample current) =>
        new(
            gatewayId,
            current.MachineCode,
            current.SampledAtUtc,
            DateTimeOffset.UtcNow,
            ToInt16(current.MachineState),
            CalculateDelta(previous.ShotOkCount, current.ShotOkCount),
            CalculateDelta(previous.ShotNgCount, current.ShotNgCount),
            current.CycleTimeMs,
            CalculateDelta(previous.RunTimeTotalMs, current.RunTimeTotalMs),
            CalculateDelta(previous.StopTimeTotalMs, current.StopTimeTotalMs),
            CalculateDelta(previous.ErrorTimeTotalMs, current.ErrorTimeTotalMs));

    private static long? ReadInt64(IReadOnlyDictionary<string, SignalValueDto> signals, params string[] signalCodes)
    {
        var signal = signalCodes
            .Select(signalCode => signals.TryGetValue(signalCode, out var value) ? value : null)
            .FirstOrDefault(value => value?.Value is not null);
        if (signal?.Value is null)
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

    private static int? ReadInt32(IReadOnlyDictionary<string, SignalValueDto> signals, params string[] signalCodes)
    {
        var value = ReadInt64(signals, signalCodes);
        if (value is null || value < int.MinValue || value > int.MaxValue)
        {
            return null;
        }

        return (int)value.Value;
    }

    private static short? ToInt16(int? value)
    {
        if (value is null || value < short.MinValue || value > short.MaxValue)
        {
            return null;
        }

        return (short)value.Value;
    }

    private static int? CalculateDelta(long? previous, long? current)
    {
        if (previous is null || current is null || current < previous)
        {
            return null;
        }

        var delta = current.Value - previous.Value;
        return delta <= int.MaxValue ? (int)delta : null;
    }

    private sealed record RawDeviceSample(
        string MachineCode,
        DateTimeOffset SampledAtUtc,
        int? MachineState,
        long? ShotOkCount,
        long? ShotNgCount,
        int? CycleTimeMs,
        long? RunTimeTotalMs,
        long? StopTimeTotalMs,
        long? ErrorTimeTotalMs);
}
