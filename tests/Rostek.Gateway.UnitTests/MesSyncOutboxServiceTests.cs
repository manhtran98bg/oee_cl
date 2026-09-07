using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.UnitTests.Fakes;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class MesSyncOutboxServiceTests
{
    [Fact]
    public async Task Enqueue_creates_python_schema_outbox_without_production_context_when_not_required()
    {
        var repository = new FakeOeeLocalRepository();
        repository.RawIntervals.Add(Raw("M16-01", 10, shotOkTotal: 100, runTimeTotalSec: 10));
        repository.Contexts.Add(TestContext("M16-01", 10));
        repository.Periods.Add(TestPeriod("M16-01", 10));
        var service = CreateService([Raw("M16-01", 15, shotOkTotal: 108, runTimeTotalSec: 15)], repository, requireProductionContext: false);

        var result = await service.EnqueueLocalOeeAsync("GW-M16-01", CancellationToken.None);

        Assert.Equal(1, result.RawIntervalCount);
        Assert.True(result.ProductionMetricCount > 0);
        Assert.True(result.OutboxCount > 0);
        Assert.Contains(repository.OutboxMessages, message => message.Topic == "metric.second" && message.SourceTable == "production_metric");
        Assert.Contains(repository.OutboxMessages, message => message.Topic == "product_metric" && message.SourceTable == "product_metric");
        Assert.Contains(repository.OutboxMessages, message => message.PayloadJson.Contains("\"machine\":\"M16-01\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Enqueue_skips_without_production_context_when_required()
    {
        var repository = new FakeOeeLocalRepository();
        var service = CreateService([Raw("M16-01", 15, shotOkTotal: 108)], repository, requireProductionContext: true);

        var result = await service.EnqueueLocalOeeAsync("GW-M16-01", CancellationToken.None);

        Assert.Equal(0, result.RawIntervalCount);
        Assert.Empty(repository.OutboxMessages);
    }

    private static MesSyncOutboxService CreateService(
        IReadOnlyList<PlcRawInterval> rawIntervals,
        FakeOeeLocalRepository repository,
        bool requireProductionContext)
    {
        var options = Options.Create(new MesSyncOptions
        {
            SyncIntervalMs = 5000,
            RequireProductionContext = requireProductionContext
        });
        var rawIntervalService = new FakeOeeRawIntervalService(rawIntervals);
        var store = new ProductionContextStore();
        store.Replace(repository.Contexts.ToDictionary(context => context.Machine, StringComparer.OrdinalIgnoreCase));
        var metricBuilder = new OeeMetricBuilder(repository, store, NullLogger<OeeMetricBuilder>.Instance);
        return new MesSyncOutboxService(
            options,
            rawIntervalService,
            metricBuilder,
            repository,
            NullLogger<MesSyncOutboxService>.Instance);
    }

    private static ProductionContext TestContext(string machine, long startAt) =>
        new()
        {
            Machine = machine,
            Mode = OeeTestProductionContext.Mode,
            OrderId = OeeTestProductionContext.OrderId,
            ServerOrderId = OeeTestProductionContext.ServerOrderId,
            ActivePeriodId = $"{machine}-{OeeTestProductionContext.PeriodId}",
            CurrentPlcPeriodIndex = OeeTestProductionContext.PlcPeriodIndex,
            ProductsJson = OeeTestProductionContext.ProductsJson,
            TagsJson = OeeTestProductionContext.TagsJson,
            ExtraJson = OeeTestProductionContext.ExtraJson,
            Status = "active",
            UpdatedAt = startAt
        };

    private static ProductionPeriod TestPeriod(string machine, long startAt) =>
        new()
        {
            PeriodId = $"{machine}-{OeeTestProductionContext.PeriodId}",
            Machine = machine,
            PlcPeriodIndex = OeeTestProductionContext.PlcPeriodIndex,
            Mode = OeeTestProductionContext.Mode,
            OrderId = OeeTestProductionContext.OrderId,
            ServerOrderId = OeeTestProductionContext.ServerOrderId,
            ProductsJson = OeeTestProductionContext.ProductsJson,
            TagsJson = OeeTestProductionContext.TagsJson,
            ExtraJson = OeeTestProductionContext.ExtraJson,
            StartAt = startAt,
            Status = "active",
            CreatedAt = startAt,
            UpdatedAt = startAt
        };

    private static PlcRawInterval Raw(string machine, long readAt, long shotOkTotal = 0, long runTimeTotalSec = 0) =>
        new()
        {
            Machine = machine,
            ReadAt = readAt,
            PlcPeriodIndex = OeeTestProductionContext.PlcPeriodIndex,
            RunState = "run",
            ShotOkTotal = shotOkTotal,
            CycleTimeMs = 1000,
            RunTimeTotalSec = runTimeTotalSec,
            PeriodActive = 1
        };

    private sealed class FakeOeeRawIntervalService(IReadOnlyList<PlcRawInterval> rawIntervals) : IOeeRawIntervalService
    {
        public Task<IReadOnlyList<PlcRawInterval>> CaptureAsync(TimeSpan interval, bool requireProductionContext, CancellationToken cancellationToken) =>
            Task.FromResult(requireProductionContext ? [] : rawIntervals);
    }
}
