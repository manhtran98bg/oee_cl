using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.MesSync;

public sealed class ProductionCommandService(
    IMesSyncOutboxRepository repository,
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

        var machineCode = request.MachineCode.Trim();
        var occurredAtUnixTimeSeconds = request.OccurredAtUnixTimeSeconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var context = await repository.GetProductionContextAsync(machineCode, cancellationToken) ?? new ProductionContext
        {
            MachineCode = machineCode
        };

        context.CommandCode = request.CommandCode.Trim();
        context.Status = status;
        context.ProductionOrderCode = Normalize(request.ProductionOrderCode);
        context.OperatorCode = Normalize(request.OperatorCode);
        context.ReasonCode = Normalize(request.ReasonCode);
        context.Note = Normalize(request.Note);
        context.UpdatedUnixTimeSeconds = occurredAtUnixTimeSeconds;

        switch (status)
        {
            case Domain.Enums.ProductionContextStatus.Started:
                context.StartedUnixTimeSeconds = occurredAtUnixTimeSeconds;
                context.PausedUnixTimeSeconds = null;
                context.StoppedUnixTimeSeconds = null;
                break;
            case Domain.Enums.ProductionContextStatus.Paused:
                context.PausedUnixTimeSeconds = occurredAtUnixTimeSeconds;
                break;
            case Domain.Enums.ProductionContextStatus.Stopped:
                context.StoppedUnixTimeSeconds = occurredAtUnixTimeSeconds;
                break;
        }

        await repository.SaveProductionContextAsync(context, cancellationToken);
        logger.LogInformation("Production command accepted. MachineCode={MachineCode}, CommandCode={CommandCode}, Status={Status}", context.MachineCode, context.CommandCode, context.Status);

        return new ProductionCommandResponse(true, context.MachineCode, context.CommandCode, context.Status.ToString(), "Accepted");
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class MesSyncOutboxService(
    IOptions<MesSyncOptions> options,
    IOeeRawIntervalService rawIntervalService,
    IOeeMetricBuilder metricBuilder,
    IMesSyncOutboxRepository repository,
    ILogger<MesSyncOutboxService> logger) : IMesSyncOutboxService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<int> EnqueueSecondlyMetricsAsync(string gatewayId, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Max(1000, options.Value.SyncIntervalMs));
        var rawIntervals = await rawIntervalService.CaptureAsync(interval, cancellationToken);
        if (rawIntervals.Count == 0)
        {
            logger.LogDebug("No new OEE raw intervals available to build MES metrics");
            return 0;
        }

        var contexts = await repository.ListActiveProductionContextsAsync(cancellationToken);
        var metrics = await metricBuilder.BuildMetricsAsync(gatewayId, rawIntervals, contexts, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), cancellationToken);
        if (metrics.Count == 0)
        {
            logger.LogDebug("No OEE secondly metrics available to enqueue");
            return 0;
        }

        var payloadJson = JsonSerializer.Serialize(new { data = metrics }, JsonOptions);
        var nowUnixTimeSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await repository.AddOutboxMessageAsync(new MesSyncOutboxMessage
        {
            Topic = MesSyncTopics.MetricSecond,
            Endpoint = MesSyncEndpoints.SecondlyProductionSync,
            PayloadJson = payloadJson,
            CreatedUnixTimeSeconds = nowUnixTimeSeconds,
            UpdatedUnixTimeSeconds = nowUnixTimeSeconds,
            NextAttemptUnixTimeSeconds = nowUnixTimeSeconds
        }, cancellationToken);

        logger.LogInformation("Enqueued {MetricCount} OEE secondly metrics for MES sync", metrics.Count);
        return metrics.Count;
    }
}

public sealed class MesSyncDispatcher(
    IOptions<MesSyncOptions> options,
    IMesSyncOutboxRepository repository,
    IMesServerClient serverClient,
    ILogger<MesSyncDispatcher> logger) : IMesSyncDispatcher
{
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        var current = options.Value;
        if (!current.Enabled)
        {
            return 0;
        }

        var nowUnixTimeSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var messages = await repository.TakePendingAsync(nowUnixTimeSeconds, Math.Clamp(current.BatchSize, 1, 1000), cancellationToken);
        var synced = 0;
        foreach (var message in messages)
        {
            try
            {
                await serverClient.SendAsync(message.Endpoint, message.PayloadJson, cancellationToken);
                await repository.MarkSyncedAsync(message.Id, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), cancellationToken);
                synced++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var nextAttemptUnixTimeSeconds = CalculateNextAttempt(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), message.RetryCount);
                await repository.MarkFailedAsync(message.Id, ex.Message, nextAttemptUnixTimeSeconds, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), cancellationToken);
                logger.LogWarning(ex, "MES sync failed. OutboxId={OutboxId}, Topic={Topic}, NextAttemptUnixTimeSeconds={NextAttemptUnixTimeSeconds}", message.Id, message.Topic, nextAttemptUnixTimeSeconds);
            }
        }

        return synced;
    }

    private static long CalculateNextAttempt(long nowUnixTimeSeconds, int retryCount)
    {
        var delaySeconds = Math.Min(60, Math.Pow(2, Math.Min(6, retryCount)));
        return nowUnixTimeSeconds + (long)delaySeconds;
    }
}
