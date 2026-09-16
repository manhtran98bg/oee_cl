using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.UnitTests.Support;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class ProductionCommandServiceTests
{
    [Fact]
    public async Task Batch_start_can_create_two_active_sessions_for_same_machine()
    {
        var repository = new InMemoryOeeLocalRepository();
        var cache = new ProductionContextCache();
        var service = CreateService(repository, cache);

        var response = await service.HandleBatchAsync(new ProductionCommandBatchRequest
        {
            GatewayId = "GW-M16-01",
            CreatedAt = 100,
            Items =
            [
                StartItem("CMD-001", "M16-01", "MO-001", "SP-001", cycleTime: 16m),
                StartItem("CMD-002", "M16-01", "MO-002", "SP-002", cycleTime: 12m)
            ]
        }, CancellationToken.None);

        Assert.True(response.Accepted);
        Assert.Equal(2, response.AcceptedCount);
        Assert.Equal(2, repository.Contexts.Count);
        Assert.Equal(2, repository.Periods.Count);
        Assert.All(repository.Contexts, context => Assert.Equal("M16-01", context.Machine));
        Assert.Contains(repository.Contexts, context => context.OrderId == "MO-001" && context.SessionId == "M16-01-MO-001-100");
        Assert.Contains(repository.Contexts, context => context.OrderId == "MO-002" && context.SessionId == "M16-01-MO-002-100");
        Assert.Equal(2, cache.GetCapturableByMachine("M16-01").Count);
    }

    [Fact]
    public async Task Start_same_machine_order_reuses_active_session_and_updates_products()
    {
        var repository = new InMemoryOeeLocalRepository();
        var service = CreateService(repository);
        await service.HandleBatchAsync(Batch(100, [StartItem("CMD-001", "M16-01", "MO-001", "SP-001", 16m)]), CancellationToken.None);

        var response = await service.HandleBatchAsync(Batch(120, [StartItem("CMD-002", "M16-01", "MO-001", "SP-002", 12m)]), CancellationToken.None);

        Assert.True(response.Accepted);
        var context = Assert.Single(repository.Contexts);
        Assert.Equal("M16-01-MO-001-100", context.SessionId);
        Assert.Contains("SP-002", context.ProductsJson, StringComparison.Ordinal);
        using var productsJson = JsonDocument.Parse(context.ProductsJson);
        Assert.Equal(12m, productsJson.RootElement[0].GetProperty("cycle_time").GetDecimal());
    }

    [Fact]
    public async Task Start_after_stopped_order_creates_new_session()
    {
        var repository = new InMemoryOeeLocalRepository();
        var service = CreateService(repository);
        await service.HandleBatchAsync(Batch(100, [StartItem("CMD-001", "M16-01", "MO-001", "SP-001", 16m)]), CancellationToken.None);
        await service.HandleBatchAsync(Batch(110, [CommandItem("CMD-002", "M16-01", "stop", "MO-001")]), CancellationToken.None);

        var response = await service.HandleBatchAsync(Batch(120, [StartItem("CMD-003", "M16-01", "MO-001", "SP-001", 16m)]), CancellationToken.None);

        Assert.True(response.Accepted);
        var context = Assert.Single(repository.Contexts);
        Assert.Equal("M16-01-MO-001-120", context.SessionId);
        Assert.Equal("active", context.Status);
        Assert.Contains(repository.Contexts, context => context.SessionId == "M16-01-MO-001-120" && context.Status == "active");
        Assert.Contains(repository.Periods, period => period.PeriodId == "M16-01-MO-001-100" && period.Status == "stopped");
        Assert.Contains(repository.Periods, period => period.PeriodId == "M16-01-MO-001-120" && period.Status == "active");
    }

    [Fact]
    public async Task Pause_and_stop_target_context_by_machine_and_order()
    {
        var repository = new InMemoryOeeLocalRepository();
        var service = CreateService(repository);
        await service.HandleBatchAsync(Batch(100, [
            StartItem("CMD-001", "M16-01", "MO-001", "SP-001", 16m),
            StartItem("CMD-002", "M16-01", "MO-002", "SP-002", 12m)
        ]), CancellationToken.None);

        var pause = await service.HandleBatchAsync(Batch(110, [CommandItem("CMD-003", "M16-01", "pause", "MO-002")]), CancellationToken.None);
        var stop = await service.HandleBatchAsync(Batch(120, [CommandItem("CMD-004", "M16-01", "stop", "MO-001")]), CancellationToken.None);

        Assert.True(pause.Accepted);
        Assert.True(stop.Accepted);
        Assert.DoesNotContain(repository.Contexts, context => context.OrderId == "MO-001");
        Assert.Equal("pause", repository.Contexts.Single(context => context.OrderId == "MO-002").Status);
        Assert.Equal("stopped", repository.Periods.Single(period => period.OrderId == "MO-001").Status);
    }

    [Fact]
    public async Task Stop_last_order_removes_machine_from_current_contexts()
    {
        var repository = new InMemoryOeeLocalRepository();
        var cache = new ProductionContextCache();
        var service = CreateService(repository, cache);
        await service.HandleBatchAsync(Batch(100, [StartItem("CMD-001", "M16-01", "MO-001", "SP-001", 16m)]), CancellationToken.None);

        var response = await service.HandleBatchAsync(Batch(110, [CommandItem("CMD-002", "M16-01", "stop", "MO-001")]), CancellationToken.None);

        Assert.True(response.Accepted);
        Assert.Empty(repository.Contexts);
        Assert.Empty(cache.GetCapturableByMachine("M16-01"));
        Assert.Equal("stopped", repository.Periods.Single().Status);
    }

    [Fact]
    public async Task Stop_creates_final_session_metric_outbox_and_updates_order_quantity_cache()
    {
        var repository = new InMemoryOeeLocalRepository();
        var contextCache = new ProductionContextCache();
        var orderQuantityCache = new OrderQuantityCache();
        var service = CreateService(repository, contextCache, orderQuantityCache);
        await service.HandleBatchAsync(Batch(100, [StartItem("CMD-001", "M16-01", "MO-001", "SP-001", 10m)]), CancellationToken.None);
        var context = repository.Contexts.Single();
        context.BaselineRawId = "baseline";
        context.BaselineCapturedAt = 100;
        context.BaselineShotOkTotal = 10;
        context.BaselineShotNgTotal = 1;
        context.BaselineRunTimeTotalSec = 5;
        context.BaselineStopTimeTotalSec = 0;
        context.BaselineErrorTimeTotalSec = 0;
        await repository.SaveProductionContextAsync(context, CancellationToken.None);
        contextCache.Upsert(context);
        repository.RawIntervals.Add(new PlcRawInterval
        {
            Machine = "M16-01",
            ReadAt = 120,
            PlcPeriodIndex = 1,
            RunState = OeeRunStates.Run,
            PeriodActive = 1,
            ShotOkTotal = 20,
            ShotNgTotal = 2,
            RunTimeTotalSec = 20,
            StopTimeTotalSec = 0,
            ErrorTimeTotalSec = 0,
            CycleTimeMs = 10000
        });

        var response = await service.HandleBatchAsync(Batch(130, [CommandItem("CMD-002", "M16-01", "stop", "MO-001")]), CancellationToken.None);

        Assert.True(response.Accepted);
        var metric = Assert.Single(repository.ProductionMetrics, item => item.BucketType == "session" && item.IsFinal);
        Assert.Equal(11, metric.ActualQty);
        Assert.Equal(11, metric.TotalQty);
        Assert.Equal(11, orderQuantityCache.GetCompletedQty("M16-01", "MO-001"));
        Assert.Contains(repository.SyncOutboxMessages, item => item.Topic == SyncOutboxTopics.ProductionMetric);
    }

    [Fact]
    public async Task Batch_item_reject_does_not_fail_other_items()
    {
        var service = CreateService();

        var response = await service.HandleBatchAsync(new ProductionCommandBatchRequest
        {
            GatewayId = "GW-M16-01",
            CreatedAt = 100,
            Items =
            [
                StartItem("CMD-001", "M16-01", "MO-001", "SP-001", 16m),
                StartItem("CMD-002", "M16-99", "MO-002", "SP-002", 12m)
            ]
        }, CancellationToken.None);

        Assert.False(response.Accepted);
        Assert.Equal(1, response.AcceptedCount);
        Assert.Equal(1, response.RejectedCount);
        Assert.Contains(response.Items, item => item.Accepted);
        Assert.Contains(response.Items, item => !item.Accepted && item.MachineCode == "M16-99");
    }

    private static ProductionCommandService CreateService(
        InMemoryOeeLocalRepository? repository = null,
        ProductionContextCache? cache = null,
        OrderQuantityCache? orderQuantityCache = null)
    {
        repository ??= new InMemoryOeeLocalRepository();
        cache ??= new ProductionContextCache();
        orderQuantityCache ??= new OrderQuantityCache();
        return new ProductionCommandService(
            repository,
            cache,
            orderQuantityCache,
            new ProductionMetricBuilder(
                repository,
                cache,
                Options.Create(new MesSyncOptions()),
                NullLogger<ProductionMetricBuilder>.Instance),
            Options.Create(new MesSyncOptions { ProductionMetricsEnabled = true }),
            new InMemoryConfigRepository("M16-01"),
            NullLogger<ProductionCommandService>.Instance);
    }

    private static ProductionCommandBatchRequest Batch(long createdAt, IReadOnlyList<ProductionCommandItemRequest> items) =>
        new()
        {
            GatewayId = "GW-M16-01",
            CreatedAt = createdAt,
            Items = items
        };

    private static ProductionCommandItemRequest StartItem(string commandCode, string machine, string orderId, string productCode, decimal cycleTime) =>
        new()
        {
            CommandCode = commandCode,
            MachineCode = machine,
            Action = "start",
            OrderId = orderId,
            Products =
            [
                new ProductionCommandProduct
                {
                    ProductCode = productCode,
                    MoldCode = "KHUON-001",
                    Cavity = 1,
                    CycleTime = cycleTime,
                    TargetQty = 100
                }
            ]
        };

    private static ProductionCommandItemRequest CommandItem(string commandCode, string machine, string action, string orderId) =>
        new()
        {
            CommandCode = commandCode,
            MachineCode = machine,
            Action = action,
            OrderId = orderId
        };
}
