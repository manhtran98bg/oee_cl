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
            "MES OEE sync started. SyncIntervalMs={SyncIntervalMs}, MachineStateEventSyncIntervalMs={MachineStateEventSyncIntervalMs}, BatchSize={BatchSize}, RequireProductionContext={RequireProductionContext}",
            (int)interval.TotalMilliseconds,
            Math.Max(1000, current.MachineStateEventSyncIntervalMs),
            current.BatchSize,
            current.RequireProductionContext);

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
            var eventIntervalSeconds = Math.Max(1, mesSyncOptions.Value.MachineStateEventSyncIntervalMs / 1000);
            if (now - _lastMachineStateEventDispatchAt >= eventIntervalSeconds)
            {
                var dispatcher = scope.ServiceProvider.GetRequiredService<ISyncOutboxDispatcher>();
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
