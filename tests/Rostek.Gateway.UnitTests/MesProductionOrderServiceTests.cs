using Microsoft.Extensions.Logging.Abstractions;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Application.Scheduling;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.UnitTests.Support;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class MesProductionOrderServiceTests
{
    [Fact]
    public async Task Refresh_reads_all_pages_and_keeps_multiple_orders_for_machine()
    {
        var client = new StubClient((page, _) => Task.FromResult(page switch
        {
            1 => Page(1, 2, [Order("ORDER-1", "M-01")]),
            2 => Page(2, null, [Order("ORDER-2", "m-01")]),
            _ => throw new InvalidOperationException("Unexpected page")
        }));
        var service = CreateService(client, new TestRuntimeConfigurationProvider(("M-01", true)));

        var result = await service.RefreshAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.All(result.Value.Items, order => Assert.True(order.CanCreateSession));
        Assert.Equal([1, 2], client.RequestedPages);
    }

    [Fact]
    public async Task Failed_refresh_keeps_previous_complete_snapshot()
    {
        var fail = false;
        var client = new StubClient((page, _) =>
        {
            if (fail && page == 2)
            {
                throw new HttpRequestException("MES unavailable");
            }

            return Task.FromResult(page == 1
                ? Page(1, 2, [Order("ORDER-1", "M-01")])
                : Page(2, null, [Order("ORDER-2", "M-01")]));
        });
        var service = CreateService(client, new TestRuntimeConfigurationProvider(("M-01", true)));
        Assert.True((await service.RefreshAsync(CancellationToken.None)).Succeeded);
        fail = true;

        var failedResult = await service.RefreshAsync(CancellationToken.None);

        Assert.False(failedResult.Succeeded);
        Assert.Equal(2, service.GetSnapshot().Items.Count);
    }

    [Fact]
    public async Task Order_with_invalid_product_or_unapplied_machine_cannot_create_session()
    {
        var client = new StubClient((_, _) => Task.FromResult(Page(1, null,
        [
            Order("ORDER-1", "M-01", cavity: 0),
            Order("ORDER-2", "M-02")
        ])));
        var service = CreateService(client, new TestRuntimeConfigurationProvider(("M-01", true)));

        var result = await service.RefreshAsync(CancellationToken.None);

        var invalidProduct = Assert.Single(result.Value!.Items, order => order.OrderId == "ORDER-1");
        Assert.False(invalidProduct.CanCreateSession);
        Assert.Contains("Cavity", invalidProduct.Message, StringComparison.Ordinal);
        var unapplied = Assert.Single(result.Value.Items, order => order.OrderId == "ORDER-2");
        Assert.False(unapplied.CanCreateSession);
        Assert.Equal("Machine is not applied.", unapplied.Message);
    }

    [Fact]
    public async Task CreateSessions_refreshes_and_maps_selected_orders_to_one_command_batch()
    {
        var commandService = new RecordingCommandService();
        var client = new StubClient((_, _) => Task.FromResult(Page(1, null,
        [
            Order("ORDER-1", "M-01", source: "oi", productCode: "SP-01", cavity: 4, cycleTime: 16),
            Order("ORDER-2", "M-01", source: "oee", productCode: "SP-02", cavity: 2, cycleTime: 12)
        ])));
        var service = CreateService(
            client,
            new TestRuntimeConfigurationProvider(("M-01", true)),
            commandService: commandService);

        var result = await service.CreateSessionsAsync(
            "GW-01",
            "M-01",
            ["ORDER-1", "ORDER-2"],
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.AcceptedCount);
        var batch = Assert.Single(commandService.Batches);
        Assert.Equal("GW-01", batch.GatewayId);
        Assert.Collection(
            batch.Items!,
            item =>
            {
                Assert.Equal("ORDER-1", item.OrderId);
                Assert.Equal("oi", item.QuantityUpdateSource);
                var product = Assert.Single(item.Products!);
                Assert.Equal("SP-01", product.ProductCode);
                Assert.Equal(4, product.Cavity);
                Assert.Equal(16, product.CycleTime);
            },
            item => Assert.Equal("ORDER-2", item.OrderId));
    }

    [Fact]
    public async Task Existing_context_is_reported_without_creating_duplicate_command()
    {
        var contextCache = new ProductionContextCache();
        contextCache.Upsert(new ProductionContext
        {
            SessionId = "SESSION-1",
            Machine = "M-01",
            OrderId = "ORDER-1",
            Status = "active"
        });
        var commandService = new RecordingCommandService();
        var service = CreateService(
            new StubClient((_, _) => Task.FromResult(Page(1, null, [Order("ORDER-1", "M-01")]))),
            new TestRuntimeConfigurationProvider(("M-01", true)),
            contextCache,
            commandService);

        var result = await service.CreateSessionsAsync("GW-01", "M-01", ["ORDER-1"], CancellationToken.None);

        Assert.True(result.Succeeded);
        var response = Assert.Single(result.Value!.Items);
        Assert.True(response.Accepted);
        Assert.Equal("SESSION-1", response.SessionId);
        Assert.Empty(commandService.Batches);
    }

    private static MesProductionOrderService CreateService(
        IMesProductionOrderClient client,
        TestRuntimeConfigurationProvider runtimeProvider,
        IProductionContextCache? contextCache = null,
        IProductionCommandService? commandService = null) =>
        new(
            client,
            new MesProductionOrderCache(),
            runtimeProvider,
            contextCache ?? new ProductionContextCache(),
            commandService ?? new RecordingCommandService(),
            NullLogger<MesProductionOrderService>.Instance);

    private static MesProductionOrderPageDto Page(
        int page,
        int? nextPage,
        IReadOnlyList<MesProductionOrderDto> orders) =>
        new(1, "GW-01", 100, new MesProductionOrderPaginationDto(page, 100, orders.Count, nextPage is null ? page : nextPage.Value, nextPage), orders);

    private static MesProductionOrderDto Order(
        string orderId,
        string machineCode,
        string source = "oee",
        string productCode = "SP-01",
        decimal cavity = 1,
        decimal cycleTime = 10) =>
        new(orderId, machineCode, source, "producing",
        [
            new MesProductionOrderProductDto(productCode, "MOLD-01", cavity, cycleTime, 1000)
        ]);

    private sealed class StubClient(Func<int, string, Task<MesProductionOrderPageDto>> fetch) : IMesProductionOrderClient
    {
        public List<int> RequestedPages { get; } = [];

        public async Task<MesProductionOrderPageDto> FetchPageAsync(int page, string productionStatus, CancellationToken cancellationToken)
        {
            RequestedPages.Add(page);
            Assert.Equal("producing", productionStatus);
            return await fetch(page, productionStatus);
        }
    }

    private sealed class RecordingCommandService : IProductionCommandService
    {
        public List<ProductionCommandBatchRequest> Batches { get; } = [];

        public Task<ProductionCommandBatchResponse> HandleBatchAsync(ProductionCommandBatchRequest request, CancellationToken cancellationToken)
        {
            Batches.Add(request);
            var items = request.Items!.Select(item => new ProductionCommandResponse(
                true,
                item.MachineCode,
                item.CommandCode,
                "active",
                item.OrderId,
                $"{item.MachineCode}-{item.OrderId}-SESSION",
                "Accepted")).ToList();
            return Task.FromResult(new ProductionCommandBatchResponse(
                true,
                1,
                request.GatewayId,
                request.CreatedAt,
                items.Count,
                0,
                items));
        }

        public Task<ProductionCommandResponse> HandleAsync(ProductionCommandRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProductionRotationResult> RotateActiveSessionsAsync(
            string gatewayId,
            string machineCode,
            long executedAt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
