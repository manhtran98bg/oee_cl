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
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var current = mesSyncOptions.Value;
        if (!current.Enabled)
        {
            logger.LogInformation("MES sync is disabled");
            return;
        }

        var interval = TimeSpan.FromMilliseconds(Math.Max(1000, current.SyncIntervalMs));
        logger.LogInformation("MES sync started. SyncIntervalMs={SyncIntervalMs}, BatchSize={BatchSize}", (int)interval.TotalMilliseconds, current.BatchSize);

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
            var outboxService = scope.ServiceProvider.GetRequiredService<IMesSyncOutboxService>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IMesSyncDispatcher>();
            await outboxService.EnqueueSecondlyMetricsAsync(gatewayOptions.Value.GatewayId, stoppingToken);
            var synced = await dispatcher.DispatchPendingAsync(stoppingToken);
            if (synced > 0)
            {
                logger.LogInformation("MES sync sent {MessageCount} outbox messages", synced);
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
