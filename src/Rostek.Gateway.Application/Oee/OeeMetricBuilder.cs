using Microsoft.Extensions.Logging;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Domain.Enums;

namespace Rostek.Gateway.Application.Oee;

public interface IOeeMetricBuilder
{
    Task<IReadOnlyList<OeeMetric>> BuildMetricsAsync(
        string gatewayId,
        IReadOnlyCollection<PlcRawInterval> currentRawIntervals,
        IReadOnlyDictionary<string, ProductionContext> productionContexts,
        long nowUnixTimeSeconds,
        CancellationToken cancellationToken);
}

public sealed class OeeMetricBuilder(
    IOeeRawIntervalRepository repository,
    ILogger<OeeMetricBuilder> logger) : IOeeMetricBuilder
{
    private const string MetricVersion = "1";

    public async Task<IReadOnlyList<OeeMetric>> BuildMetricsAsync(
        string gatewayId,
        IReadOnlyCollection<PlcRawInterval> currentRawIntervals,
        IReadOnlyDictionary<string, ProductionContext> productionContexts,
        long nowUnixTimeSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayId);

        var metrics = new List<OeeMetric>();
        foreach (var current in currentRawIntervals.OrderBy(raw => raw.MachineCode).ThenBy(raw => raw.ReadAtUnixTimeSeconds))
        {
            if (!productionContexts.TryGetValue(current.MachineCode, out var context) ||
                context.Status == ProductionContextStatus.Stopped)
            {
                continue;
            }

            var previous = await repository.GetPreviousAsync(current.MachineCode, current.ReadAtUnixTimeSeconds, cancellationToken);
            if (previous is null)
            {
                logger.LogDebug("No previous raw interval found for machine {MachineCode} at unix timestamp {ReadAtUnixTimeSeconds}", current.MachineCode, current.ReadAtUnixTimeSeconds);
                continue;
            }

            var delta = CreateDeltaSample(previous, current);
            metrics.Add(CreateMetric(gatewayId, context, delta, nowUnixTimeSeconds));
        }

        return metrics;
    }

    private static OeeDeltaSample CreateDeltaSample(PlcRawInterval previous, PlcRawInterval current) =>
        new(
            current.MachineCode,
            current.ReadAtUnixTimeSeconds,
            current.MachineState,
            CalculateDelta(previous.ShotOkTotal, current.ShotOkTotal),
            CalculateDelta(previous.ShotNgTotal, current.ShotNgTotal),
            current.CycleTimeMs,
            CalculateDelta(previous.RunTimeTotal, current.RunTimeTotal),
            CalculateDelta(previous.StopTimeTotal, current.StopTimeTotal),
            CalculateDelta(previous.ErrorTimeTotal, current.ErrorTimeTotal));

    private static OeeMetric CreateMetric(string gatewayId, ProductionContext context, OeeDeltaSample delta, long nowUnixTimeSeconds)
    {
        var total = AddNullable(delta.ShotOkDelta, delta.ShotNgDelta);
        var runTimeSeconds = ToSeconds(delta.RunTimeDeltaMs);
        var stopTimeSeconds = ToSeconds(delta.StopTimeDeltaMs);
        var errorTimeSeconds = ToSeconds(delta.ErrorTimeDeltaMs);
        var prodTimeSeconds = AddNullable(runTimeSeconds, stopTimeSeconds, errorTimeSeconds);
        var cycleSeconds = ToSeconds(delta.CycleTimeMs);

        var availability = Divide(runTimeSeconds, prodTimeSeconds);
        decimal? quality = total is > 0 && delta.ShotOkDelta is int ok ? Divide(ok, total.Value) : null;
        decimal? performance = total is > 0 && cycleSeconds is not null && runTimeSeconds is > 0
            ? ClampRatio(total.Value * cycleSeconds.Value / runTimeSeconds.Value)
            : null;
        decimal? oee = availability is not null && performance is not null && quality is not null
            ? ClampRatio(availability.Value * performance.Value * quality.Value)
            : null;

        return new OeeMetric(
            Mode: context.Status.ToString(),
            Machine: delta.MachineCode,
            Version: MetricVersion,
            OrderId: context.ProductionOrderCode,
            Tag: context.CommandCode,
            Total: total,
            NgQty: delta.ShotNgDelta,
            RunTime: runTimeSeconds,
            ErrorTime: errorTimeSeconds,
            StopTime: stopTimeSeconds,
            ProdTime: prodTimeSeconds,
            Availability: availability,
            Performance: performance,
            Quality: quality,
            Cycle: cycleSeconds,
            Oee: oee,
            ProductId: null,
            StartAt: context.StartedUnixTimeSeconds,
            EndAt: delta.SampledAtUnixTimeSeconds,
            UpdatedAt: nowUnixTimeSeconds);
    }

    private static int? CalculateDelta(long? previous, long? current)
    {
        if (previous is null || current is null)
        {
            return null;
        }

        var delta = current.Value - previous.Value;
        if (delta < 0)
        {
            return 0;
        }

        return delta <= int.MaxValue ? (int)delta : null;
    }

    private static int? AddNullable(int? left, int? right) =>
        left is null && right is null ? null : (left ?? 0) + (right ?? 0);

    private static decimal? AddNullable(params decimal?[] values) =>
        values.All(value => value is null) ? null : values.Sum(value => value ?? 0);

    private static decimal? ToSeconds(int? milliseconds) =>
        milliseconds is null ? null : Math.Round(milliseconds.Value / 1000m, 3, MidpointRounding.AwayFromZero);

    private static decimal? Divide(decimal? numerator, decimal? denominator) =>
        numerator is null || denominator is null || denominator == 0 ? null : ClampRatio(numerator.Value / denominator.Value);

    private static decimal Divide(int numerator, int denominator) =>
        ClampRatio(numerator / (decimal)denominator);

    private static decimal ClampRatio(decimal value) =>
        Math.Min(1m, Math.Max(0m, Math.Round(value, 6, MidpointRounding.AwayFromZero)));
}
