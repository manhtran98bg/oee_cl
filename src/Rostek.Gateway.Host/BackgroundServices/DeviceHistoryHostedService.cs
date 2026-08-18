using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.History;
using Rostek.Gateway.Host.Options;

namespace Rostek.Gateway.Host.BackgroundServices;

public sealed class DeviceHistoryHostedService(
    IOptions<HistoryOptions> historyOptions,
    IOptions<GatewayOptions> gatewayOptions,
    IDeviceHistorySampler sampler,
    IDeviceSampleWriter writer,
    ILogger<DeviceHistoryHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan RetentionCleanupInterval = TimeSpan.FromDays(1);
    private bool _schemaReady;
    private bool _retentionDisabledLogged;
    private DateTimeOffset _nextRetentionCleanupUtc = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var current = historyOptions.Value;
        if (!current.Enabled)
        {
            logger.LogInformation("Device history is disabled");
            return;
        }

        if (!current.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Device history provider {Provider} is not supported", current.Provider);
            return;
        }

        var interval = TimeSpan.FromMilliseconds(Math.Max(1000, current.SampleIntervalMs));
        logger.LogInformation("Device history PostgreSQL writer started. SampleIntervalMs={SampleIntervalMs}, BatchSize={BatchSize}", (int)interval.TotalMilliseconds, current.BatchSize);

        try
        {
            var nextTickUtc = GetNextAlignedTick(DateTimeOffset.UtcNow, interval);
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = nextTickUtc - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, stoppingToken);
                }

                await WriteOnceAsync(stoppingToken);
                nextTickUtc = nextTickUtc.Add(interval);
                var now = DateTimeOffset.UtcNow;
                while (nextTickUtc <= now)
                {
                    nextTickUtc = nextTickUtc.Add(interval);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task WriteOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            var current = historyOptions.Value;
            await EnsureSchemaAsync(stoppingToken);
            await CleanupRetentionIfDueAsync(current, stoppingToken);
            await sampler.SampleAndWriteAsync(gatewayOptions.Value.GatewayId, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Device history write failed");
        }
    }

    private async Task EnsureSchemaAsync(CancellationToken stoppingToken)
    {
        if (_schemaReady)
        {
            return;
        }

        await writer.EnsureSchemaAsync(stoppingToken);
        _schemaReady = true;
    }

    private async Task CleanupRetentionIfDueAsync(HistoryOptions current, CancellationToken stoppingToken)
    {
        if (current.RetentionDays <= 0)
        {
            if (!_retentionDisabledLogged)
            {
                logger.LogWarning("Device history retention cleanup is disabled because RetentionDays={RetentionDays}", current.RetentionDays);
                _retentionDisabledLogged = true;
            }

            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now < _nextRetentionCleanupUtc)
        {
            return;
        }

        _nextRetentionCleanupUtc = now.Add(RetentionCleanupInterval);
        var cutoffUtc = now.AddDays(-current.RetentionDays);
        try
        {
            var deleted = await writer.DeleteOlderThanAsync(cutoffUtc, stoppingToken);
            logger.LogInformation("Device history retention cleanup deleted {DeletedRowCount} rows older than {CutoffUtc}", deleted, cutoffUtc);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Device history retention cleanup failed");
        }
    }

    public static TimeSpan CalculateDelayToNextAlignedTick(DateTimeOffset now, TimeSpan interval)
    {
        var delay = GetNextAlignedTick(now, interval) - now;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private static DateTimeOffset GetNextAlignedTick(DateTimeOffset now, TimeSpan interval)
    {
        var intervalMs = Math.Max(1000, (long)interval.TotalMilliseconds);
        var nowMs = now.ToUnixTimeMilliseconds();
        var remainder = nowMs % intervalMs;
        var delayMs = remainder == 0 ? 0 : intervalMs - remainder;
        return now.AddMilliseconds(delayMs);
    }
}
