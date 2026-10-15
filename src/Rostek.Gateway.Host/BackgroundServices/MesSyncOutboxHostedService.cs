using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Host.Options;

namespace Rostek.Gateway.Host.BackgroundServices;

public sealed class MesSyncOutboxHostedService(
    IOptions<MesSyncOptions> options,
    IOptions<GatewayOptions> gatewayOptions,
    IServiceScopeFactory scopeFactory,
    IRealtimeSnapshotSyncStatusStore statusStore,
    ILogger<MesSyncOutboxHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        logger.LogInformation(
            "MES outbox dispatch started. RealtimeIntervalMs={RealtimeIntervalMs}, StateEventIntervalMs={StateEventIntervalMs}, ProductionMetricIntervalMs={ProductionMetricIntervalMs}",
            Math.Max(250, options.Value.OutboxDispatchIntervalMs),
            Math.Max(1000, options.Value.MachineStateEventSyncIntervalMs),
            Math.Max(1000, options.Value.ProductionMetricSyncIntervalMs));

        await Task.WhenAll(
            RunTopicLoopAsync(
                SyncOutboxTopics.RealtimeSnapshot,
                () => options.Value.RealtimeSnapshotsEnabled,
                () => Math.Max(250, options.Value.OutboxDispatchIntervalMs),
                stoppingToken),
            RunTopicLoopAsync(
                SyncOutboxTopics.MachineStateEvent,
                () => options.Value.MachineStateEventsEnabled,
                () => Math.Max(1000, options.Value.MachineStateEventSyncIntervalMs),
                stoppingToken),
            RunTopicLoopAsync(
                SyncOutboxTopics.ProductionMetric,
                () => options.Value.ProductionMetricsEnabled,
                () => Math.Max(1000, options.Value.ProductionMetricSyncIntervalMs),
                stoppingToken));
    }

    private async Task RunTopicLoopAsync(
        string topic,
        Func<bool> isEnabled,
        Func<int> getIntervalMs,
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (isEnabled())
            {
                await DispatchOnceAsync(topic, stoppingToken);
            }

            try
            {
                await Task.Delay(getIntervalMs(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task DispatchOnceAsync(string topic, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<ISyncOutboxDispatcher>();
            var result = await dispatcher.DispatchPendingAsync(
                gatewayOptions.Value.GatewayId,
                [topic],
                stoppingToken);
            if (topic == SyncOutboxTopics.RealtimeSnapshot)
            {
                if (result.FailedCount > 0)
                {
                    statusStore.MarkDropped($"{result.FailedCount} realtime snapshot outbox items failed.");
                }
                else if (result.SentCount > 0)
                {
                    statusStore.MarkSuccess(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), result.SentCount);
                }
            }

            if (result.SentCount > 0 || result.FailedCount > 0)
            {
                logger.LogDebug(
                    "MES outbox dispatch completed. Topic={Topic}, Sent={Sent}, Failed={Failed}",
                    topic,
                    result.SentCount,
                    result.FailedCount);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "MES outbox dispatch failed. Topic={Topic}", topic);
        }
    }
}
