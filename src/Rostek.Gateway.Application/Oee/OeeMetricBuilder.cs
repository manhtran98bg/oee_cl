using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Oee;

public sealed class OeeMetricBuilder(
    IOeeLocalRepository repository,
    IProductionContextCache productionContextCache,
    ILogger<OeeMetricBuilder> logger) : IOeeMetricBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<OeeBuildResult> BuildMetricsAsync(IReadOnlyCollection<PlcRawInterval> currentRawIntervals, CancellationToken cancellationToken)
    {
        var productionMetrics = new List<ProductionMetric>();
        var productMetrics = new List<ProductMetric>();
        var downtimeEvents = new List<DowntimeEvent>();

        foreach (var current in currentRawIntervals.OrderBy(raw => raw.Machine, StringComparer.OrdinalIgnoreCase).ThenBy(raw => raw.ReadAt))
        {
            var context = productionContextCache.Get(current.Machine);
            if (context is null || !context.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var period = await repository.GetProductionPeriodAsync(context.ActivePeriodId, cancellationToken);
            if (period is null || !period.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (current.PlcPeriodIndex != period.PlcPeriodIndex)
            {
                continue;
            }

            var previous = await repository.GetPreviousRawInPeriodAsync(
                current.Machine,
                current.PlcPeriodIndex,
                period.StartAt,
                current.ReadAt,
                cancellationToken);
            if (previous is null)
            {
                logger.LogDebug("No previous raw interval found for machine {Machine} at unix timestamp {ReadAt}", current.Machine, current.ReadAt);
                continue;
            }

            var products = ParseProducts(period.ProductsJson);
            foreach (var product in products)
            {
                productionMetrics.Add(await UpsertSecondMetricAsync(period, product, previous, current, cancellationToken));
                if (await UpsertStateMetricAsync(period, product, previous, current, cancellationToken) is { } stateMetric)
                {
                    productionMetrics.Add(stateMetric);
                }

                productionMetrics.Add(await UpsertAccumulatedMetricAsync(OeeMetricTypes.Period, period, product, ZeroRaw(period.Machine, period.PlcPeriodIndex, period.StartAt), current, period.StartAt, cancellationToken));
                productionMetrics.Add(await UpsertBucketMetricAsync(OeeMetricTypes.Hour, period, product, current, StartOfHour(current.ReadAt), cancellationToken));
                productionMetrics.Add(await UpsertBucketMetricAsync(OeeMetricTypes.Day, period, product, current, StartOfDay(current.ReadAt), cancellationToken));

                var productMetric = await RebuildProductMetricAsync(period, product, cancellationToken);
                if (productMetric is not null)
                {
                    await repository.UpsertProductMetricAsync(productMetric, cancellationToken);
                    productMetrics.Add(productMetric);
                }
            }

            if (await UpsertDowntimeAsync(period, current, cancellationToken) is { } downtimeEvent)
            {
                downtimeEvents.Add(downtimeEvent);
            }
        }

        return new OeeBuildResult(productionMetrics, productMetrics, downtimeEvents);
    }

    private async Task<ProductionMetric> UpsertSecondMetricAsync(
        ProductionPeriod period,
        OeeProductDefinition product,
        PlcRawInterval previous,
        PlcRawInterval current,
        CancellationToken cancellationToken)
    {
        var metric = CreateMetric(OeeMetricTypes.Second, period, product, previous, current, previous.ReadAt, current.ReadAt, runState: string.Empty);
        await repository.UpsertProductionMetricAsync(metric, cancellationToken);
        return metric;
    }

    private async Task<ProductionMetric?> UpsertStateMetricAsync(
        ProductionPeriod period,
        OeeProductDefinition product,
        PlcRawInterval previous,
        PlcRawInterval current,
        CancellationToken cancellationToken)
    {
        if (!OeeRunStates.IsKnownProcessState(current.RunState))
        {
            return null;
        }

        var startAt = previous.ReadAt;
        var endAt = current.ReadAt;
        var metric = CreateMetric(OeeMetricTypes.State, period, product, previous, current, startAt, endAt, current.RunState, useStateDuration: true);
        var latest = await repository.GetLatestStateMetricAsync(current.Machine, period.PeriodId, product.ProductId, current.RunState, startAt, cancellationToken);
        if (latest is not null && startAt - latest.EndAt <= 60)
        {
            latest.EndAt = endAt;
            latest.TotalQty += metric.TotalQty;
            latest.NgQty += metric.NgQty;
            latest.PlanQty += metric.PlanQty;
            latest.ProdTimeSec += metric.ProdTimeSec;
            latest.RunTimeSec += metric.RunTimeSec;
            latest.StopTimeSec += metric.StopTimeSec;
            latest.ErrorTimeSec += metric.ErrorTimeSec;
            RecalculateOee(latest, product.EffectiveCycleTime);
            latest.UpdatedAt = current.ReadAt;
            await repository.UpsertProductionMetricAsync(latest, cancellationToken);
            return latest;
        }

        await repository.UpsertProductionMetricAsync(metric, cancellationToken);
        return metric;
    }

    private async Task<ProductionMetric> UpsertBucketMetricAsync(
        string metricType,
        ProductionPeriod period,
        OeeProductDefinition product,
        PlcRawInterval current,
        long bucketStartAt,
        CancellationToken cancellationToken)
    {
        var baseline = await repository.GetFirstRawInRangeAsync(current.Machine, current.PlcPeriodIndex, bucketStartAt, current.ReadAt, cancellationToken)
                       ?? current;
        return await UpsertAccumulatedMetricAsync(metricType, period, product, baseline, current, bucketStartAt, cancellationToken);
    }

    private async Task<ProductionMetric> UpsertAccumulatedMetricAsync(
        string metricType,
        ProductionPeriod period,
        OeeProductDefinition product,
        PlcRawInterval baseline,
        PlcRawInterval current,
        long startAt,
        CancellationToken cancellationToken)
    {
        var metric = CreateMetric(metricType, period, product, baseline, current, startAt, current.ReadAt, runState: string.Empty);
        await repository.UpsertProductionMetricAsync(metric, cancellationToken);
        return metric;
    }

    private async Task<ProductMetric?> RebuildProductMetricAsync(ProductionPeriod period, OeeProductDefinition product, CancellationToken cancellationToken)
    {
        var metrics = await repository.ListPeriodMetricsAsync(period.OrderId, product.ProductId, cancellationToken);
        if (metrics.Count == 0)
        {
            return null;
        }

        var first = metrics.Min(metric => metric.StartAt);
        var last = metrics.Max(metric => metric.EndAt);
        var productMetric = new ProductMetric
        {
            Machine = period.Machine,
            ServerOrderId = period.ServerOrderId,
            OrderId = period.OrderId,
            ProductId = product.ProductId,
            StartAt = first,
            EndAt = last,
            TotalQty = metrics.Sum(metric => metric.TotalQty),
            NgQty = metrics.Sum(metric => metric.NgQty),
            CountCheckQty = metrics.Sum(metric => metric.TotalQty + metric.NgQty),
            PlanQty = metrics.Sum(metric => metric.PlanQty),
            ProdTimeSec = metrics.Sum(metric => metric.ProdTimeSec),
            RunTimeSec = metrics.Sum(metric => metric.RunTimeSec),
            StopTimeSec = metrics.Sum(metric => metric.StopTimeSec),
            ErrorTimeSec = metrics.Sum(metric => metric.ErrorTimeSec),
            ActualCycleSec = product.EffectiveCycleTime,
            TargetQty = product.Target,
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        productMetric.CreatedAt = productMetric.UpdatedAt;
        RecalculateOee(productMetric, product.EffectiveCycleTime);
        return productMetric;
    }

    private async Task<DowntimeEvent?> UpsertDowntimeAsync(ProductionPeriod period, PlcRawInterval current, CancellationToken cancellationToken)
    {
        var open = await repository.GetOpenDowntimeEventAsync(current.Machine, period.PeriodId, cancellationToken);
        if (OeeRunStates.IsDowntime(current.RunState))
        {
            if (open is not null && open.State.Equals(current.RunState, StringComparison.OrdinalIgnoreCase))
            {
                open.EndAt = current.ReadAt;
                open.DurationSec = SafeInt(current.ReadAt - open.StartAt);
                open.UpdatedAt = current.ReadAt;
                await repository.UpsertDowntimeEventAsync(open, cancellationToken);
                return open;
            }

            if (open is not null)
            {
                open.EndAt = current.ReadAt;
                open.DurationSec = SafeInt(current.ReadAt - open.StartAt);
                open.UpdatedAt = current.ReadAt;
                await repository.UpsertDowntimeEventAsync(open, cancellationToken);
            }

            await repository.UpsertDowntimeEventAsync(new DowntimeEvent
            {
                Machine = current.Machine,
                OrderId = period.OrderId,
                ServerOrderId = period.ServerOrderId,
                PeriodId = period.PeriodId,
                PlcPeriodIndex = current.PlcPeriodIndex,
                State = current.RunState,
                StartAt = current.ReadAt,
                EndAt = 0,
                DurationSec = 0,
                OrderExtraJson = period.ExtraJson,
                CreatedAt = current.ReadAt,
                UpdatedAt = current.ReadAt
            }, cancellationToken);
            return await repository.GetOpenDowntimeEventAsync(current.Machine, period.PeriodId, cancellationToken);
        }

        if (open is null)
        {
            return null;
        }

        open.EndAt = current.ReadAt;
        open.DurationSec = SafeInt(current.ReadAt - open.StartAt);
        open.UpdatedAt = current.ReadAt;
        await repository.UpsertDowntimeEventAsync(open, cancellationToken);
        return open;
    }

    private static ProductionMetric CreateMetric(
        string metricType,
        ProductionPeriod period,
        OeeProductDefinition product,
        PlcRawInterval baseline,
        PlcRawInterval current,
        long startAt,
        long endAt,
        string runState,
        bool useStateDuration = false)
    {
        var goodQty = ApplyGain(Delta(baseline.ShotOkTotal, current.ShotOkTotal), product.Gain);
        var ngQty = ApplyGain(Delta(baseline.ShotNgTotal, current.ShotNgTotal), product.Gain);
        var elapsedSec = SafeInt(Math.Max(0, endAt - startAt));

        var metric = new ProductionMetric
        {
            MetricType = metricType,
            Machine = current.Machine,
            OrderId = period.OrderId,
            ServerOrderId = period.ServerOrderId,
            PeriodId = period.PeriodId,
            ProductId = product.ProductId,
            RunState = runState,
            StartAt = startAt,
            EndAt = endAt,
            TotalQty = goodQty,
            NgQty = ngQty,
            ActualCycleSec = product.EffectiveCycleTime,
            CreatedAt = current.ReadAt,
            UpdatedAt = current.ReadAt
        };

        if (useStateDuration)
        {
            metric.RunTimeSec = runState == OeeRunStates.Run ? elapsedSec : 0;
            metric.StopTimeSec = runState == OeeRunStates.Stop ? elapsedSec : 0;
            metric.ErrorTimeSec = runState == OeeRunStates.Error ? elapsedSec : 0;
        }
        else
        {
            metric.RunTimeSec = SafeInt(Delta(baseline.RunTimeTotalSec, current.RunTimeTotalSec));
            metric.StopTimeSec = SafeInt(Delta(baseline.StopTimeTotalSec, current.StopTimeTotalSec));
            metric.ErrorTimeSec = SafeInt(Delta(baseline.ErrorTimeTotalSec, current.ErrorTimeTotalSec));
        }

        metric.ProdTimeSec = metric.RunTimeSec + metric.StopTimeSec + metric.ErrorTimeSec;
        RecalculateOee(metric, product.EffectiveCycleTime);
        return metric;
    }

    private static void RecalculateOee(ProductionMetric metric, decimal cycleSec)
    {
        metric.PlanQty = cycleSec > 0 ? Math.Round(metric.ProdTimeSec / cycleSec, 6, MidpointRounding.AwayFromZero) : 0;
        metric.Availability = RatioPercent(metric.RunTimeSec, metric.ProdTimeSec);
        metric.Performance = metric.PlanQty > 0
            ? ClampPercent((metric.TotalQty + metric.NgQty) / metric.PlanQty * 100m)
            : 0;
        metric.Quality = metric.TotalQty + metric.NgQty > 0
            ? ClampPercent(metric.TotalQty / (decimal)(metric.TotalQty + metric.NgQty) * 100m)
            : 0;
        metric.Oee = Math.Round(metric.Availability * metric.Performance * metric.Quality / 10_000m, 6, MidpointRounding.AwayFromZero);
    }

    private static void RecalculateOee(ProductMetric metric, decimal cycleSec)
    {
        metric.PlanQty = cycleSec > 0 ? Math.Round(metric.ProdTimeSec / cycleSec, 6, MidpointRounding.AwayFromZero) : 0;
        metric.Availability = RatioPercent(metric.RunTimeSec, metric.ProdTimeSec);
        metric.Performance = metric.PlanQty > 0
            ? ClampPercent((metric.TotalQty + metric.NgQty) / metric.PlanQty * 100m)
            : 0;
        metric.Quality = metric.TotalQty + metric.NgQty > 0
            ? ClampPercent(metric.TotalQty / (decimal)(metric.TotalQty + metric.NgQty) * 100m)
            : 0;
        metric.Oee = Math.Round(metric.Availability * metric.Performance * metric.Quality / 10_000m, 6, MidpointRounding.AwayFromZero);
    }

    private static IReadOnlyList<OeeProductDefinition> ParseProducts(string productsJson)
    {
        try
        {
            var products = JsonSerializer.Deserialize<List<OeeProductDefinition>>(productsJson, JsonOptions);
            return products is { Count: > 0 }
                ? products.Select(NormalizeProduct).ToList()
                : [DefaultProduct()];
        }
        catch (JsonException)
        {
            return [DefaultProduct()];
        }
    }

    private static OeeProductDefinition NormalizeProduct(OeeProductDefinition product) =>
        new()
        {
            ProductId = string.IsNullOrWhiteSpace(product.ProductId) ? string.Empty : product.ProductId.Trim(),
            Gain = product.Gain <= 0 ? 1m : product.Gain,
            CycleTime = product.EffectiveCycleTime <= 0 ? 1m : product.EffectiveCycleTime,
            Target = product.Target
        };

    private static OeeProductDefinition DefaultProduct() =>
        new()
        {
            Gain = 1m,
            CycleTime = 1m
        };

    private static PlcRawInterval ZeroRaw(string machine, int plcPeriodIndex, long readAt) =>
        new()
        {
            Machine = machine,
            PlcPeriodIndex = plcPeriodIndex,
            ReadAt = readAt
        };

    private static long StartOfHour(long unixTimeSeconds)
    {
        var value = DateTimeOffset.FromUnixTimeSeconds(unixTimeSeconds).ToUniversalTime();
        return new DateTimeOffset(value.Year, value.Month, value.Day, value.Hour, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
    }

    private static long StartOfDay(long unixTimeSeconds)
    {
        var value = DateTimeOffset.FromUnixTimeSeconds(unixTimeSeconds).ToUniversalTime();
        return new DateTimeOffset(value.Year, value.Month, value.Day, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
    }

    private static long Delta(long previous, long current) => Math.Max(0, current - previous);

    private static int ApplyGain(long value, decimal gain) =>
        SafeInt(decimal.ToInt64(Math.Truncate(value * gain)));

    private static int SafeInt(long value) =>
        value > int.MaxValue ? int.MaxValue : value < int.MinValue ? int.MinValue : (int)value;

    private static decimal RatioPercent(decimal numerator, decimal denominator) =>
        denominator <= 0 ? 0 : ClampPercent(numerator / denominator * 100m);

    private static decimal ClampPercent(decimal value) =>
        Math.Min(100m, Math.Max(0m, Math.Round(value, 6, MidpointRounding.AwayFromZero)));
}
