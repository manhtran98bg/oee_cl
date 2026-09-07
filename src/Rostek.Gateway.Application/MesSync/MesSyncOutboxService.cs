using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.MesSync;

public sealed class ProductionCommandService(
    IOeeLocalRepository repository,
    IProductionContextStore productionContextStore,
    ILogger<ProductionCommandService> logger) : IProductionCommandService
{
    public async Task<ProductionCommandResponse> HandleAsync(ProductionCommandRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.MachineCode))
        {
            return new ProductionCommandResponse(false, string.Empty, request.CommandCode, null, "machine_code is required.");
        }

        if (string.IsNullOrWhiteSpace(request.CommandCode))
        {
            return new ProductionCommandResponse(false, request.MachineCode, string.Empty, null, "command_code is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Action) || !ProductionCommandActions.TryMapStatus(request.Action, out var status))
        {
            return new ProductionCommandResponse(false, request.MachineCode, request.CommandCode, null, "action must be start, pause, or stop.");
        }

        var now = request.OccurredAtUnixTimeSeconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var machine = request.MachineCode.Trim();
        var context = await repository.GetProductionContextAsync(machine, cancellationToken) ?? new ProductionContext
        {
            Machine = machine,
            Mode = OeeTestProductionContext.Mode,
            CurrentPlcPeriodIndex = OeeTestProductionContext.PlcPeriodIndex,
            ProductsJson = OeeTestProductionContext.ProductsJson,
            TagsJson = OeeTestProductionContext.TagsJson,
            ExtraJson = OeeTestProductionContext.ExtraJson
        };

        context.Status = status;
        context.OrderId = Normalize(request.ProductionOrderCode) ?? context.OrderId;
        context.ServerOrderId = context.OrderId;
        context.ActivePeriodId = Normalize(request.SessionId) ?? context.ActivePeriodId;
        context.UpdatedAt = now;

        await repository.SaveProductionContextAsync(context, cancellationToken);
        productionContextStore.Upsert(context);
        if (status == "active")
        {
            await repository.EnsureTestProductionPeriodAsync(context, now, cancellationToken);
        }

        logger.LogInformation("Production command accepted. Machine={Machine}, CommandCode={CommandCode}, Status={Status}", context.Machine, request.CommandCode.Trim(), context.Status);
        return new ProductionCommandResponse(true, context.Machine, request.CommandCode.Trim(), context.Status, "Accepted");
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class MesSyncOutboxService(
    IOptions<MesSyncOptions> options,
    IOeeRawIntervalService rawIntervalService,
    IOeeMetricBuilder metricBuilder,
    IOeeLocalRepository repository,
    ILogger<MesSyncOutboxService> logger) : IMesSyncOutboxService
{
    private const string MetricVersion = "1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MesOutboxBuildResult> EnqueueLocalOeeAsync(string gatewayId, CancellationToken cancellationToken)
    {
        var current = options.Value;
        var interval = TimeSpan.FromMilliseconds(Math.Max(1000, current.SyncIntervalMs));
        var rawIntervals = await rawIntervalService.CaptureAsync(interval, current.RequireProductionContext, cancellationToken);
        if (rawIntervals.Count == 0)
        {
            logger.LogDebug("No new PLC raw intervals available for local OEE pipeline");
            return new MesOutboxBuildResult(0, 0, 0, 0, 0);
        }

        var build = await metricBuilder.BuildMetricsAsync(rawIntervals, cancellationToken);
        var outboxCount = await EnqueueOutboxAsync(build, cancellationToken);
        logger.LogInformation(
            "Local OEE pipeline completed. RawIntervals={RawIntervalCount}, ProductionMetrics={ProductionMetricCount}, ProductMetrics={ProductMetricCount}, DowntimeEvents={DowntimeEventCount}, OutboxRows={OutboxCount}",
            rawIntervals.Count,
            build.ProductionMetricCount,
            build.ProductMetricCount,
            build.DowntimeEventCount,
            outboxCount);

        return new MesOutboxBuildResult(rawIntervals.Count, build.ProductionMetricCount, build.ProductMetricCount, build.DowntimeEventCount, outboxCount);
    }

    private async Task<int> EnqueueOutboxAsync(OeeBuildResult build, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var metric in build.ProductionMetrics)
        {
            var period = await repository.GetProductionPeriodAsync(metric.PeriodId, cancellationToken);
            if (period is null)
            {
                continue;
            }

            var topic = metric.MetricType switch
            {
                OeeMetricTypes.Second => MesSyncTopics.MetricSecond,
                OeeMetricTypes.State => MesSyncTopics.MetricState,
                OeeMetricTypes.Hour => MesSyncTopics.MetricHour,
                OeeMetricTypes.Day => MesSyncTopics.MetricDay,
                OeeMetricTypes.Period => MesSyncTopics.MetricPeriod,
                _ => $"metric.{metric.MetricType}"
            };

            await repository.EnqueueOutboxAsync(new MesSyncOutboxMessage
            {
                Topic = topic,
                SourceTable = "production_metric",
                SourceId = $"{metric.MetricType}|{metric.PeriodId}|{metric.ProductId}|{metric.RunState}|{metric.StartAt}",
                PayloadJson = JsonSerializer.Serialize(ToPayload(metric, period), JsonOptions),
                CreatedAt = metric.CreatedAt,
                UpdatedAt = metric.UpdatedAt
            }, cancellationToken);
            count++;
        }

        foreach (var metric in build.ProductMetrics)
        {
            await repository.EnqueueOutboxAsync(new MesSyncOutboxMessage
            {
                Topic = MesSyncTopics.ProductMetric,
                SourceTable = "product_metric",
                SourceId = $"{metric.OrderId}|{metric.ProductId}",
                PayloadJson = JsonSerializer.Serialize(ToPayload(metric), JsonOptions),
                CreatedAt = metric.CreatedAt,
                UpdatedAt = metric.UpdatedAt
            }, cancellationToken);
            count++;
        }

        foreach (var downtime in build.DowntimeEvents)
        {
            await repository.EnqueueOutboxAsync(new MesSyncOutboxMessage
            {
                Topic = MesSyncTopics.Downtime,
                SourceTable = "downtime_event",
                SourceId = downtime.Id,
                PayloadJson = JsonSerializer.Serialize(ToPayload(downtime), JsonOptions),
                CreatedAt = downtime.CreatedAt,
                UpdatedAt = downtime.UpdatedAt
            }, cancellationToken);
            count++;
        }

        return count;
    }

    private static OeeMetricPayload ToPayload(ProductionMetric metric, ProductionPeriod period)
    {
        var syncTarget = metric.MetricType == OeeMetricTypes.Period ? "period" : "process";
        var includeCountCheck = metric.MetricType != OeeMetricTypes.State;
        return new OeeMetricPayload(
            Mode: period.Mode,
            Machine: metric.Machine,
            Version: MetricVersion,
            PeriodId: metric.PeriodId,
            OrderId: metric.OrderId,
            Tag: OeeTestProductionContext.OrderId,
            Total: metric.TotalQty,
            RunTime: metric.RunTimeSec,
            ErrorTime: metric.ErrorTimeSec,
            StopTime: metric.StopTimeSec,
            ProdTime: metric.ProdTimeSec,
            Plan: metric.PlanQty,
            Availability: metric.Availability,
            Performance: metric.Performance,
            Quality: metric.Quality,
            Cycle: metric.ActualCycleSec,
            Oee: metric.Oee,
            ProductId: metric.ProductId,
            StartAt: metric.StartAt,
            EndAt: metric.EndAt,
            UpdatedAt: metric.UpdatedAt,
            CountCheck: includeCountCheck ? metric.TotalQty + metric.NgQty : null,
            Ng: includeCountCheck ? metric.NgQty : null,
            SyncTarget: syncTarget);
    }

    private static object ToPayload(ProductMetric metric) => new
    {
        total = metric.TotalQty,
        run_time = metric.RunTimeSec,
        error_time = metric.ErrorTimeSec,
        stop_time = metric.StopTimeSec,
        prod_time = metric.ProdTimeSec,
        count_check = metric.CountCheckQty,
        ng = metric.NgQty,
        plan = metric.PlanQty,
        A = metric.Availability,
        P = metric.Performance,
        Q = metric.Quality,
        cycle = metric.ActualCycleSec,
        OEE = metric.Oee,
        id = metric.ProductId,
        order_id = metric.OrderId,
        trial = false,
        start_at = metric.StartAt,
        end_at = metric.EndAt,
        status = metric.Status,
        updated_at = metric.UpdatedAt
    };

    private static object ToPayload(DowntimeEvent downtime) => new
    {
        machine = downtime.Machine,
        order = downtime.OrderId,
        start_time = downtime.StartAt,
        duration = downtime.DurationSec,
        error = downtime.Error,
        category = downtime.Category,
        description = downtime.Description,
        extra = downtime.OrderExtraJson
    };
}

public sealed class MesSyncDispatcher(
    ILogger<MesSyncDispatcher> logger) : IMesSyncDispatcher
{
    public Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("MES HTTP dispatch is disabled in local Python OEE schema test phase");
        return Task.FromResult(0);
    }
}
