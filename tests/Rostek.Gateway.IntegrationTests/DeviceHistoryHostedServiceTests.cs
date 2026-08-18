using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.History;
using Rostek.Gateway.Host.BackgroundServices;
using Rostek.Gateway.Host.Options;
using Xunit;

namespace Rostek.Gateway.IntegrationTests;

public sealed class DeviceHistoryHostedServiceTests
{
    [Fact]
    public void CalculateDelayToNextAlignedTick_returns_delay_to_next_interval_boundary()
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(12_345);

        var delay = DeviceHistoryHostedService.CalculateDelayToNextAlignedTick(now, TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromMilliseconds(2655), delay);
    }

    [Fact]
    public async Task Disabled_history_service_does_not_write_samples()
    {
        var sampler = new FakeDeviceHistorySampler();
        var writer = new FakeDeviceSampleWriter();
        var service = new DeviceHistoryHostedService(
            Options.Create(new HistoryOptions { Enabled = false }),
            Options.Create(new GatewayOptions { GatewayId = "GW-M16-01" }),
            sampler,
            writer,
            NullLogger<DeviceHistoryHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, sampler.WriteCount);
        Assert.Equal(0, writer.EnsureSchemaCount);
        Assert.Equal(0, writer.DeleteOlderThanCount);
    }

    [Fact]
    public async Task Retention_disabled_by_non_positive_days_does_not_delete()
    {
        var sampler = new FakeDeviceHistorySampler();
        var writer = new FakeDeviceSampleWriter();
        var service = CreateService(
            new HistoryOptions { Enabled = true, RetentionDays = 0, SampleIntervalMs = 1000 },
            sampler,
            writer);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(1300);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, writer.EnsureSchemaCount);
        Assert.Equal(0, writer.DeleteOlderThanCount);
        Assert.True(sampler.WriteCount >= 1);
    }

    [Fact]
    public async Task Enabled_history_service_deletes_old_samples_on_start()
    {
        var sampler = new FakeDeviceHistorySampler();
        var writer = new FakeDeviceSampleWriter();
        var service = CreateService(
            new HistoryOptions { Enabled = true, RetentionDays = 90, SampleIntervalMs = 1000 },
            sampler,
            writer);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(1300);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, writer.EnsureSchemaCount);
        Assert.Equal(1, writer.DeleteOlderThanCount);
        Assert.True(writer.LastCutoffUtc <= DateTimeOffset.UtcNow.AddDays(-90));
        Assert.True(sampler.WriteCount >= 1);
    }

    [Fact]
    public async Task Retention_failure_does_not_stop_sample_write()
    {
        var sampler = new FakeDeviceHistorySampler();
        var writer = new FakeDeviceSampleWriter { ThrowOnDelete = true };
        var service = CreateService(
            new HistoryOptions { Enabled = true, RetentionDays = 90, SampleIntervalMs = 1000 },
            sampler,
            writer);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(1300);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, writer.EnsureSchemaCount);
        Assert.Equal(1, writer.DeleteOlderThanCount);
        Assert.True(sampler.WriteCount >= 1);
    }

    private static DeviceHistoryHostedService CreateService(
        HistoryOptions historyOptions,
        FakeDeviceHistorySampler sampler,
        FakeDeviceSampleWriter writer) =>
        new(
            Options.Create(historyOptions),
            Options.Create(new GatewayOptions { GatewayId = "GW-M16-01" }),
            sampler,
            writer,
            NullLogger<DeviceHistoryHostedService>.Instance);

    private sealed class FakeDeviceHistorySampler : IDeviceHistorySampler
    {
        public int WriteCount { get; private set; }

        public Task<int> SampleAndWriteAsync(string gatewayId, CancellationToken cancellationToken)
        {
            WriteCount++;
            return Task.FromResult(0);
        }
    }

    private sealed class FakeDeviceSampleWriter : IDeviceSampleWriter
    {
        public int EnsureSchemaCount { get; private set; }
        public int DeleteOlderThanCount { get; private set; }
        public DateTimeOffset? LastCutoffUtc { get; private set; }
        public bool ThrowOnDelete { get; init; }

        public Task EnsureSchemaAsync(CancellationToken cancellationToken)
        {
            EnsureSchemaCount++;
            return Task.CompletedTask;
        }

        public Task WriteAsync(IReadOnlyCollection<DeviceSample> samples, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
        {
            DeleteOlderThanCount++;
            LastCutoffUtc = cutoffUtc;
            return ThrowOnDelete
                ? Task.FromException<int>(new InvalidOperationException("Retention cleanup failed."))
                : Task.FromResult(7);
        }
    }
}
