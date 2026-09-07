using System.Globalization;
using Microsoft.Extensions.Logging;
using Rostek.Gateway.Contracts.Machines;
using Rostek.Gateway.Contracts.Runtime;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Oee;

public interface IOeeRawIntervalService
{
    Task<IReadOnlyList<PlcRawInterval>> CaptureAsync(TimeSpan interval, CancellationToken cancellationToken);
}

public interface IOeeRawIntervalRepository
{
    Task<IReadOnlyList<PlcRawInterval>> InsertMissingAsync(IReadOnlyCollection<PlcRawInterval> rawIntervals, CancellationToken cancellationToken);
    Task<PlcRawInterval?> GetPreviousAsync(string machineCode, long beforeReadAtUnixTimeSeconds, CancellationToken cancellationToken);
}

public sealed class OeeRawIntervalService(
    IMachineValueReader valueReader,
    IOeeRawIntervalRepository repository,
    ILogger<OeeRawIntervalService> logger) : IOeeRawIntervalService
{
    public async Task<IReadOnlyList<PlcRawInterval>> CaptureAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        var intervalSeconds = Math.Max(1, (long)interval.TotalSeconds);
        var nowUnixTimeSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var rawIntervals = valueReader.GetSnapshots()
            .Select(snapshot => TryCreateRawInterval(snapshot, intervalSeconds, nowUnixTimeSeconds))
            .OfType<PlcRawInterval>()
            .ToList();

        if (rawIntervals.Count == 0)
        {
            logger.LogDebug("No online machine snapshots available for OEE raw interval capture");
            return [];
        }

        var inserted = await repository.InsertMissingAsync(rawIntervals, cancellationToken);
        logger.LogDebug("Captured {InsertedCount} OEE raw intervals", inserted.Count);
        return inserted;
    }

    private static PlcRawInterval? TryCreateRawInterval(MachineValueSnapshotDto snapshot, long intervalSeconds, long nowUnixTimeSeconds)
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

        var readAtUnixTimeSeconds = AlignToInterval(snapshot.LastReadUtc.Value.ToUniversalTime().ToUnixTimeSeconds(), intervalSeconds);
        return new PlcRawInterval
        {
            MachineCode = snapshot.MachineCode,
            ReadAtUnixTimeSeconds = readAtUnixTimeSeconds,
            MachineState = ReadInt32(signals, OeeSignalCodes.MachineState),
            ShotOkTotal = ReadInt64(signals, OeeSignalCodes.ShotOkCount),
            ShotNgTotal = ReadInt64(signals, OeeSignalCodes.ShotNgCount),
            CycleTimeMs = ReadInt32(signals, OeeSignalCodes.CycleTimeMs),
            RunTimeTotal = ReadInt64(signals, OeeSignalCodes.RunTimeTotal),
            StopTimeTotal = ReadInt64(signals, OeeSignalCodes.StopTimeTotal),
            ErrorTimeTotal = ReadInt64(signals, OeeSignalCodes.ErrorTimeTotal),
            CreatedUnixTimeSeconds = nowUnixTimeSeconds
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
