using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Rostek.Gateway.Application.Common;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Contracts.Runtime;

namespace Rostek.Gateway.Application.MesSync;

public sealed record MesProductionOrderProductDto(
    [property: JsonPropertyName("product_code")] string? ProductCode,
    [property: JsonPropertyName("mold_code")] string? MoldCode,
    [property: JsonPropertyName("cavity")] decimal Cavity,
    [property: JsonPropertyName("cycle_time")] decimal CycleTime,
    [property: JsonPropertyName("target_qty")] int? TargetQty);

public sealed record MesProductionOrderDto(
    [property: JsonPropertyName("order_id")] string? OrderId,
    [property: JsonPropertyName("machine_code")] string? MachineCode,
    [property: JsonPropertyName("quantity_update_source")] string? QuantityUpdateSource,
    [property: JsonPropertyName("production_status")] string? ProductionStatus,
    [property: JsonPropertyName("products")] IReadOnlyList<MesProductionOrderProductDto>? Products);

public sealed record MesProductionOrderPaginationDto(
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize,
    [property: JsonPropertyName("total_items")] int TotalItems,
    [property: JsonPropertyName("total_pages")] int TotalPages,
    [property: JsonPropertyName("next_page")] int? NextPage);

public sealed record MesProductionOrderPageDto(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("gateway_id")] string? GatewayId,
    [property: JsonPropertyName("created_at")] long CreatedAt,
    [property: JsonPropertyName("pagination")] MesProductionOrderPaginationDto? Pagination,
    [property: JsonPropertyName("items")] IReadOnlyList<MesProductionOrderDto>? Items);

public sealed record MesProductionOrderProduct(
    [property: JsonPropertyName("product_code")] string ProductCode,
    [property: JsonPropertyName("mold_code")] string? MoldCode,
    [property: JsonPropertyName("cavity")] decimal Cavity,
    [property: JsonPropertyName("cycle_time")] decimal CycleTime,
    [property: JsonPropertyName("target_qty")] int TargetQty);

public sealed record MesProductionOrderListItem(
    [property: JsonPropertyName("order_id")] string OrderId,
    [property: JsonPropertyName("machine_code")] string MachineCode,
    [property: JsonPropertyName("quantity_update_source")] string QuantityUpdateSource,
    [property: JsonPropertyName("production_status")] string ProductionStatus,
    [property: JsonPropertyName("products")] IReadOnlyList<MesProductionOrderProduct> Products,
    [property: JsonPropertyName("can_create_session")] bool CanCreateSession,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("machine_applied")] bool MachineApplied);

public sealed record MesProductionOrderSnapshot(
    [property: JsonPropertyName("gateway_id")] string? GatewayId,
    [property: JsonPropertyName("fetched_at")] long? FetchedAt,
    [property: JsonPropertyName("items")] IReadOnlyList<MesProductionOrderListItem> Items)
{
    public static MesProductionOrderSnapshot Empty { get; } = new(null, null, []);
}

public sealed record MesProductionOrderSessionResult(
    [property: JsonPropertyName("accepted_count")] int AcceptedCount,
    [property: JsonPropertyName("rejected_count")] int RejectedCount,
    [property: JsonPropertyName("items")] IReadOnlyList<ProductionCommandResponse> Items);

public interface IMesProductionOrderClient
{
    Task<MesProductionOrderPageDto> FetchPageAsync(int page, string productionStatus, CancellationToken cancellationToken);
}

public interface IMesProductionOrderCache
{
    MesProductionOrderSnapshot Current { get; }
    Task WaitForRefreshAsync(CancellationToken cancellationToken);
    void ReleaseRefresh();
    void Replace(MesProductionOrderSnapshot snapshot);
}

public interface IMesProductionOrderService
{
    Task<GatewayResult<MesProductionOrderSnapshot>> RefreshAsync(CancellationToken cancellationToken);
    MesProductionOrderSnapshot GetSnapshot();
    IReadOnlyList<MesProductionOrderListItem> GetByMachine(string machineCode);
    Task<GatewayResult<MesProductionOrderSessionResult>> CreateSessionsAsync(
        string gatewayId,
        string machineCode,
        IReadOnlyCollection<string> orderIds,
        CancellationToken cancellationToken);
}

public sealed class MesProductionOrderCache : IMesProductionOrderCache
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private MesProductionOrderSnapshot _current = MesProductionOrderSnapshot.Empty;

    public MesProductionOrderSnapshot Current => Volatile.Read(ref _current);

    public Task WaitForRefreshAsync(CancellationToken cancellationToken) =>
        _refreshLock.WaitAsync(cancellationToken);

    public void ReleaseRefresh() => _refreshLock.Release();

    public void Replace(MesProductionOrderSnapshot snapshot) =>
        Interlocked.Exchange(ref _current, snapshot);
}

public sealed class MesProductionOrderService(
    IMesProductionOrderClient client,
    IMesProductionOrderCache cache,
    IRuntimeConfigurationProvider runtimeConfigurationProvider,
    IProductionContextCache productionContextCache,
    IProductionCommandService productionCommandService,
    ILogger<MesProductionOrderService> logger) : IMesProductionOrderService
{
    private const string ProducingStatus = "producing";
    private const int MaximumPageCount = 100;

    public async Task<GatewayResult<MesProductionOrderSnapshot>> RefreshAsync(CancellationToken cancellationToken)
    {
        await cache.WaitForRefreshAsync(cancellationToken);
        try
        {
            var orders = new List<MesProductionOrderDto>();
            var visitedPages = new HashSet<int>();
            string? gatewayId = null;
            var page = 1;

            while (page > 0 && visitedPages.Count < MaximumPageCount)
            {
                if (!visitedPages.Add(page))
                {
                    throw new InvalidOperationException($"MES production order pagination repeated page {page}.");
                }

                var response = await client.FetchPageAsync(page, ProducingStatus, cancellationToken);
                if (response.SchemaVersion != 1)
                {
                    throw new InvalidOperationException($"Unsupported MES production order schema version {response.SchemaVersion}.");
                }

                gatewayId ??= Normalize(response.GatewayId);
                orders.AddRange(response.Items ?? []);

                var nextPage = response.Pagination?.NextPage;
                if (nextPage is null)
                {
                    page = 0;
                    break;
                }

                if (nextPage <= page)
                {
                    throw new InvalidOperationException($"MES production order next_page {nextPage} is invalid for page {page}.");
                }

                page = nextPage.Value;
            }

            if (visitedPages.Count >= MaximumPageCount && page > 0)
            {
                throw new InvalidOperationException($"MES production order pagination exceeded {MaximumPageCount} pages.");
            }

            var normalized = orders
                .Select(NormalizeOrder)
                .Where(order => order.ProductionStatus.Equals(ProducingStatus, StringComparison.OrdinalIgnoreCase))
                .DistinctBy(order => $"{order.MachineCode}|{order.OrderId}", StringComparer.OrdinalIgnoreCase)
                .OrderBy(order => order.MachineCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(order => order.OrderId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var snapshot = new MesProductionOrderSnapshot(
                gatewayId,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                normalized);
            cache.Replace(snapshot);

            logger.LogInformation(
                "Fetched {OrderCount} producing production orders from MES across {PageCount} pages",
                normalized.Count,
                visitedPages.Count);

            return GatewayResult<MesProductionOrderSnapshot>.Ok(Decorate(snapshot));
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "Failed to refresh MES production orders; keeping the previous RAM snapshot");
            return GatewayResult<MesProductionOrderSnapshot>.Fail(FormatFetchError(exception));
        }
        finally
        {
            cache.ReleaseRefresh();
        }
    }

    public MesProductionOrderSnapshot GetSnapshot() => Decorate(cache.Current);

    public IReadOnlyList<MesProductionOrderListItem> GetByMachine(string machineCode)
    {
        var normalizedMachineCode = Normalize(machineCode);
        if (normalizedMachineCode is null)
        {
            return [];
        }

        return GetSnapshot().Items
            .Where(order => order.MachineCode.Equals(normalizedMachineCode, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<GatewayResult<MesProductionOrderSessionResult>> CreateSessionsAsync(
        string gatewayId,
        string machineCode,
        IReadOnlyCollection<string> orderIds,
        CancellationToken cancellationToken)
    {
        var refresh = await RefreshAsync(cancellationToken);
        if (!refresh.Succeeded || refresh.Value is null)
        {
            return GatewayResult<MesProductionOrderSessionResult>.Fail(refresh.ErrorMessage ?? "MES production order refresh failed.");
        }

        var normalizedMachineCode = Normalize(machineCode);
        var selectedOrderIds = orderIds
            .Select(Normalize)
            .Where(orderId => orderId is not null)
            .Select(orderId => orderId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedMachineCode is null || selectedOrderIds.Count == 0)
        {
            return GatewayResult<MesProductionOrderSessionResult>.Fail("Machine code and at least one order are required.");
        }

        if (!IsAppliedAndEnabled(normalizedMachineCode))
        {
            return GatewayResult<MesProductionOrderSessionResult>.Fail(
                $"Machine '{normalizedMachineCode}' is not enabled in the active configuration. Validate and Apply first.");
        }

        var currentOrders = refresh.Value.Items
            .Where(order => order.MachineCode.Equals(normalizedMachineCode, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(order => order.OrderId, StringComparer.OrdinalIgnoreCase);
        var responses = new List<ProductionCommandResponse>();
        var commandItems = new List<ProductionCommandItemRequest>();

        foreach (var orderId in selectedOrderIds)
        {
            if (!currentOrders.TryGetValue(orderId, out var order))
            {
                responses.Add(Rejected(normalizedMachineCode, orderId, "Order is no longer producing or was not returned by MES."));
                continue;
            }

            var activeContext = productionContextCache.GetActive(normalizedMachineCode, orderId);
            if (activeContext is not null)
            {
                responses.Add(new ProductionCommandResponse(
                    true,
                    normalizedMachineCode,
                    CommandCode(orderId),
                    activeContext.Status,
                    orderId,
                    activeContext.SessionId,
                    "Session already exists."));
                continue;
            }

            var validationMessage = ValidateOrder(order);
            if (validationMessage is not null)
            {
                responses.Add(Rejected(normalizedMachineCode, orderId, validationMessage));
                continue;
            }

            commandItems.Add(new ProductionCommandItemRequest
            {
                CommandCode = CommandCode(orderId),
                MachineCode = normalizedMachineCode,
                Action = ProductionCommandActions.Start,
                OrderId = orderId,
                QuantityUpdateSource = order.QuantityUpdateSource,
                Products = order.Products.Select(product => new ProductionCommandProduct
                {
                    ProductCode = product.ProductCode,
                    MoldCode = product.MoldCode,
                    Cavity = product.Cavity,
                    CycleTime = product.CycleTime,
                    TargetQty = product.TargetQty
                }).ToList()
            });
        }

        if (commandItems.Count > 0)
        {
            var commandResponse = await productionCommandService.HandleBatchAsync(new ProductionCommandBatchRequest
            {
                SchemaVersion = 1,
                GatewayId = Normalize(gatewayId) ?? "GATEWAY",
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Items = commandItems
            }, cancellationToken);
            responses.AddRange(commandResponse.Items);
        }

        var orderedResponses = selectedOrderIds
            .Select(orderId => responses.First(response => response.OrderId?.Equals(orderId, StringComparison.OrdinalIgnoreCase) == true))
            .ToList();
        var result = new MesProductionOrderSessionResult(
            orderedResponses.Count(response => response.Accepted),
            orderedResponses.Count(response => !response.Accepted),
            orderedResponses);
        return GatewayResult<MesProductionOrderSessionResult>.Ok(result);
    }

    private MesProductionOrderSnapshot Decorate(MesProductionOrderSnapshot snapshot)
    {
        var items = snapshot.Items.Select(order =>
        {
            var applied = IsAppliedAndEnabled(order.MachineCode);
            var context = productionContextCache.GetActive(order.MachineCode, order.OrderId);
            var validationMessage = ValidateOrder(order);
            var message = context is not null
                ? "Session already exists."
                : !applied
                    ? "Machine is not applied."
                    : validationMessage;

            return order with
            {
                CanCreateSession = context is null && applied && validationMessage is null,
                Message = message,
                SessionId = context?.SessionId,
                MachineApplied = applied
            };
        }).ToList();

        return snapshot with { Items = items };
    }

    private bool IsAppliedAndEnabled(string machineCode) =>
        runtimeConfigurationProvider.Current.Machines.TryGetValue(machineCode, out var machine) && machine.Enabled;

    private static string? ValidateOrder(MesProductionOrderListItem order)
    {
        if (string.IsNullOrWhiteSpace(order.OrderId)) return "Order ID is missing.";
        if (string.IsNullOrWhiteSpace(order.MachineCode)) return "Machine code is missing.";
        if (!QuantityUpdateSources.TryNormalize(order.QuantityUpdateSource, out _)) return "Quantity update source must be oee or oi.";
        if (order.Products.Count == 0) return "Products are missing.";
        if (order.Products.Any(product => string.IsNullOrWhiteSpace(product.ProductCode))) return "Product code is missing.";
        if (order.Products.Any(product => product.Cavity <= 0)) return "Cavity must be greater than zero.";
        if (order.Products.Any(product => product.CycleTime <= 0)) return "Cycle time must be greater than zero.";
        return null;
    }

    private static MesProductionOrderListItem NormalizeOrder(MesProductionOrderDto order)
    {
        var source = Normalize(order.QuantityUpdateSource) ?? string.Empty;
        QuantityUpdateSources.TryNormalize(source, out var normalizedSource);
        return new MesProductionOrderListItem(
            Normalize(order.OrderId) ?? string.Empty,
            Normalize(order.MachineCode) ?? string.Empty,
            normalizedSource ?? source.ToLowerInvariant(),
            Normalize(order.ProductionStatus)?.ToLowerInvariant() ?? string.Empty,
            (order.Products ?? []).Select(product => new MesProductionOrderProduct(
                Normalize(product.ProductCode) ?? string.Empty,
                Normalize(product.MoldCode),
                product.Cavity,
                product.CycleTime,
                Math.Max(0, product.TargetQty ?? 0))).ToList(),
            false,
            null,
            null,
            false);
    }

    private static ProductionCommandResponse Rejected(string machineCode, string orderId, string message) =>
        new(false, machineCode, CommandCode(orderId), null, orderId, null, message);

    private static string CommandCode(string orderId) =>
        $"MES-FETCH-{orderId}-{Guid.NewGuid():N}";

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatFetchError(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized } =>
            "MES production order request was rejected: 401 Unauthorized. Check MesSync:BearerToken.",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden } =>
            "MES production order request was rejected: 403 Forbidden. The MES account lacks production order read permission.",
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.ServiceUnavailable } =>
            "MES production orders are unavailable: 503 Service Unavailable.",
        TaskCanceledException =>
            "MES production order request timed out.",
        _ => $"Failed to fetch MES production orders: {exception.Message}"
    };
}
