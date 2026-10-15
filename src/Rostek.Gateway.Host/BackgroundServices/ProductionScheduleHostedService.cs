using Rostek.Gateway.Application.Scheduling;

namespace Rostek.Gateway.Host.BackgroundServices;

public sealed class ProductionScheduleHostedService(
    IServiceScopeFactory scopes,
    ILogger<ProductionScheduleHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IProductionScheduleRunner>().RunDueAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Production schedule tick failed; pending executions will resume on the next tick");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
