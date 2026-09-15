using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Host.Options;

namespace Rostek.Gateway.Host.BackgroundServices;

public sealed class MesSyncHostedService(
    IOptions<MesSyncOptions> mesSyncOptions,
    IOptions<GatewayOptions> gatewayOptions,
    IServiceScopeFactory scopeFactory,
    ILogger<MesSyncHostedService> logger) : BackgroundService
{
    private long _lastMachineStateEventDispatchAt;
    private long _lastProductionMetricDispatchAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var current = mesSyncOptions.Value;
        if (!current.Enabled)
        {
            logger.LogInformation("MES sync is disabled");
            return;
        }

        var interval = TimeSpan.FromMilliseconds(Math.Max(1000, current.SyncIntervalMs));
        logger.LogInformation(
            "MES OEE sync started. SyncIntervalMs={SyncIntervalMs}, MachineStateEventSyncIntervalMs={MachineStateEventSyncIntervalMs}, ProductionMetricSyncIntervalMs={ProductionMetricSyncIntervalMs}, BatchSize={BatchSize}, RequireProductionContext={RequireProductionContext}, RealtimeSnapshotsEnabled={RealtimeSnapshotsEnabled}, MachineStateEventsEnabled={MachineStateEventsEnabled}, ProductionMetricsEnabled={ProductionMetricsEnabled}",
            (int)interval.TotalMilliseconds,
            Math.Max(1000, current.MachineStateEventSyncIntervalMs),
            Math.Max(1000, current.ProductionMetricSyncIntervalMs),
            current.BatchSize,
            current.RequireProductionContext,
            current.RealtimeSnapshotsEnabled,
            current.MachineStateEventsEnabled,
            current.ProductionMetricsEnabled);

        using var timer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var realtimeSyncService = scope.ServiceProvider.GetRequiredService<IRealtimeSnapshotSyncService>();
            await realtimeSyncService.SyncAsync(gatewayOptions.Value.GatewayId, stoppingToken);

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var current = mesSyncOptions.Value;
            var dispatcher = scope.ServiceProvider.GetRequiredService<ISyncOutboxDispatcher>();

            var eventIntervalSeconds = Math.Max(1, current.MachineStateEventSyncIntervalMs / 1000);
            if (current.MachineStateEventsEnabled && now - _lastMachineStateEventDispatchAt >= eventIntervalSeconds)
            {
                var result = await dispatcher.DispatchPendingAsync(
                    gatewayOptions.Value.GatewayId,
                    [SyncOutboxTopics.MachineStateEvent],
                    stoppingToken);
                _lastMachineStateEventDispatchAt = now;
                logger.LogDebug(
                    "Machine state event outbox dispatch tick completed. Sent={Sent}, Failed={Failed}",
                    result.SentCount,
                    result.FailedCount);
            }

            var metricIntervalSeconds = Math.Max(1, current.ProductionMetricSyncIntervalMs / 1000);
            if (current.ProductionMetricsEnabled && now - _lastProductionMetricDispatchAt >= metricIntervalSeconds)
            {
                var result = await dispatcher.DispatchPendingAsync(
                    gatewayOptions.Value.GatewayId,
                    [SyncOutboxTopics.ProductionMetric],
                    stoppingToken);
                _lastProductionMetricDispatchAt = now;
                logger.LogDebug(
                    "Production metric outbox dispatch tick completed. Sent={Sent}, Failed={Failed}",
                    result.SentCount,
                    result.FailedCount);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MES sync cycle failed");
        }
    }
}
