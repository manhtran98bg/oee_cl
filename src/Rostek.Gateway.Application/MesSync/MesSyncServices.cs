using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.MesSync;

public sealed class ProductionCommandService(
    IOeeLocalRepository repository,
    IProductionContextCache productionContextCache,
    IConfigRepository configRepository,
    ILogger<ProductionCommandService> logger) : IProductionCommandService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<ProductionCommandBatchResponse> HandleBatchAsync(ProductionCommandBatchRequest request, CancellationToken cancellationToken)
    {
        var now = request.CreatedAt > 0 ? request.CreatedAt : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var items = new List<ProductionCommandResponse>();
        foreach (var item in request.Items ?? [])
        {
            items.Add(await HandleItemAsync(item, now, cancellationToken));
        }

        var acceptedCount = items.Count(item => item.Accepted);
        var response = new ProductionCommandBatchResponse(
            Accepted: acceptedCount == items.Count,
            SchemaVersion: request.SchemaVersion,
            GatewayId: request.GatewayId,
            CreatedAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            AcceptedCount: acceptedCount,
            RejectedCount: items.Count - acceptedCount,
            Items: items);

        logger.LogInformation(
            "Production command batch handled. GatewayId={GatewayId}, CreatedAt={CreatedAt}, Items={ItemCount}, Accepted={AcceptedCount}, Rejected={RejectedCount}",
            request.GatewayId,
            now,
            items.Count,
            response.AcceptedCount,
            response.RejectedCount);
        return response;
    }

    public async Task<ProductionCommandResponse> HandleAsync(ProductionCommandRequest request, CancellationToken cancellationToken)
    {
        var item = new ProductionCommandItemRequest
        {
            CommandCode = request.CommandCode,
            MachineCode = request.MachineCode,
            Action = request.Action,
            OrderId = request.ProductionOrderCode,
            Products = request.Products
        };
        var now = request.OccurredAtUnixTimeSeconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return await HandleItemAsync(item, now, cancellationToken);
    }

    private async Task<ProductionCommandResponse> HandleItemAsync(
        ProductionCommandItemRequest request,
        long now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.MachineCode))
        {
            return Reject(string.Empty, request.CommandCode, request.OrderId, null, "machine_code is required.");
        }

        if (string.IsNullOrWhiteSpace(request.CommandCode))
        {
            return Reject(request.MachineCode, string.Empty, request.OrderId, null, "command_code is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Action) || !ProductionCommandActions.TryMapStatus(request.Action, out var status))
        {
            return Reject(request.MachineCode, request.CommandCode, request.OrderId, null, "action must be start, pause, or stop.");
        }

        var machine = request.MachineCode.Trim();
        if (await configRepository.GetMachineByCodeAsync(machine, includeDetails: false, cancellationToken) is null)
        {
            return Reject(machine, request.CommandCode, request.OrderId, null, $"machine_code '{machine}' was not found in Gateway configuration.");
        }

        var orderId = Normalize(request.OrderId);
        if (orderId is null)
        {
            return Reject(machine, request.CommandCode, request.OrderId, null, "order_id is required.");
        }

        var response = status switch
        {
            "active" => await StartAsync(request, machine, orderId, now, cancellationToken),
            "pause" => await PauseAsync(request, machine, orderId, now, cancellationToken),
            "stopped" => await StopAsync(request, machine, orderId, now, cancellationToken),
            _ => Reject(machine, request.CommandCode, orderId, null, "action must be start, pause, or stop.")
        };

        logger.LogInformation(
            "Production command item handled. CommandCode={CommandCode}, Machine={Machine}, OrderId={OrderId}, Accepted={Accepted}, Status={Status}, Session={Session}, Message={Message}",
            response.CommandCode,
            response.MachineCode,
            response.OrderId,
            response.Accepted,
            response.Status,
            response.SessionId,
            response.Message);
        return response;
    }

    private async Task<ProductionCommandResponse> StartAsync(
        ProductionCommandItemRequest request,
        string machine,
        string orderId,
        long now,
        CancellationToken cancellationToken)
    {
        var productValidation = ValidateProducts(request.Products);
        if (productValidation is not null)
        {
            return Reject(machine, request.CommandCode, orderId, null, productValidation);
        }

        var context = await repository.GetActiveProductionContextAsync(machine, orderId, cancellationToken);
        var isNewSession = context is null;
        if (context is null)
        {
            var sessionId = BuildSessionId(machine, orderId, now);
            var plcPeriodIndex = await repository.GetNextPlcPeriodIndexAsync(machine, orderId, cancellationToken);
            context = new ProductionContext
            {
                SessionId = sessionId,
                Machine = machine,
                ServerOrderId = orderId,
                ActivePeriodStartAt = now,
                CurrentPlcPeriodIndex = plcPeriodIndex
            };
        }

        context.Status = "active";
        context.OrderId = orderId;
        context.ProductsJson = JsonSerializer.Serialize(ToOeeProducts(request.Products!), JsonOptions);
        context.ExtraJson = "{}";
        if (isNewSession)
        {
            ClearBaseline(context);
        }

        context.UpdatedAt = now;
        await repository.SaveProductionContextAsync(context, cancellationToken);
        await repository.EnsureProductionPeriodAsync(context, context.ActivePeriodStartAt, cancellationToken);
        productionContextCache.Upsert(context);
        return new ProductionCommandResponse(true, context.Machine, request.CommandCode.Trim(), context.Status, context.OrderId, context.SessionId, "Accepted");
    }

    private async Task<ProductionCommandResponse> PauseAsync(
        ProductionCommandItemRequest request,
        string machine,
        string orderId,
        long now,
        CancellationToken cancellationToken)
    {
        var context = await repository.GetActiveProductionContextAsync(machine, orderId, cancellationToken);
        if (context is null)
        {
            return Reject(machine, request.CommandCode, orderId, null, "active production context was not found for machine/order.");
        }

        context.Status = "pause";
        context.UpdatedAt = now;
        await repository.SaveProductionContextAsync(context, cancellationToken);
        productionContextCache.Upsert(context);
        return new ProductionCommandResponse(true, context.Machine, request.CommandCode.Trim(), context.Status, context.OrderId, context.SessionId, "Accepted");
    }

    private async Task<ProductionCommandResponse> StopAsync(
        ProductionCommandItemRequest request,
        string machine,
        string orderId,
        long now,
        CancellationToken cancellationToken)
    {
        var context = await repository.GetActiveProductionContextAsync(machine, orderId, cancellationToken);
        if (context is null)
        {
            return Reject(machine, request.CommandCode, orderId, null, "active production context was not found for machine/order.");
        }

        await repository.CloseProductionPeriodAsync(context.SessionId, now, "stopped", cancellationToken);
        await repository.DeleteProductionContextAsync(context.SessionId, cancellationToken);
        productionContextCache.Remove(context.SessionId);
        return new ProductionCommandResponse(true, context.Machine, request.CommandCode.Trim(), "stopped", context.OrderId, context.SessionId, "Accepted");
    }

    private static void ClearBaseline(ProductionContext context)
    {
        context.BaselineRawId = string.Empty;
        context.BaselineCapturedAt = 0;
        context.BaselineShotOkTotal = 0;
        context.BaselineShotNgTotal = 0;
        context.BaselineRunTimeTotalSec = 0;
        context.BaselineStopTimeTotalSec = 0;
        context.BaselineErrorTimeTotalSec = 0;
        context.BaselineCycleTimeMs = 0;
    }

    private static ProductionCommandResponse Reject(string machine, string? commandCode, string? orderId, string? sessionId, string message) =>
        new(false, machine, Normalize(commandCode) ?? string.Empty, null, Normalize(orderId), sessionId, message);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ValidateProducts(IReadOnlyList<ProductionCommandProduct>? products)
    {
        if (products is not { Count: > 0 })
        {
            return "products is required when action is start.";
        }

        foreach (var product in products)
        {
            if (string.IsNullOrWhiteSpace(product.ProductCode))
            {
                return "products[].product_code is required.";
            }

            if (product.Cavity <= 0)
            {
                return "products[].cavity must be greater than 0.";
            }

            if (product.CycleTime <= 0)
            {
                return "products[].cycle_time must be greater than 0.";
            }
        }

        return null;
    }

    private static IReadOnlyList<OeeProductDefinition> ToOeeProducts(IReadOnlyList<ProductionCommandProduct> products) =>
        products.Select(product => new OeeProductDefinition
        {
            ProductId = product.ProductCode.Trim(),
            MoldCode = Normalize(product.MoldCode),
            Gain = product.Cavity,
            CycleTime = product.CycleTime,
            Target = product.TargetQty
        }).ToList();

    private static string BuildSessionId(string machine, string orderId, long occurredAt) =>
        $"{Slug(machine)}-{Slug(orderId)}-{occurredAt}";

    private static string Slug(string value)
    {
        var chars = value.Trim().Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_').ToArray();
        return new string(chars);
    }
}

public sealed class RealtimeSnapshotBuilder(
    IOeeLocalRepository repository,
    IProductionContextCache productionContextCache,
    ILogger<RealtimeSnapshotBuilder> logger) : IRealtimeSnapshotBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RealtimeSnapshotBuildResult> BuildAsync(
        string gatewayId,
        IReadOnlyCollection<PlcRawInterval> rawIntervals,
        long createdAt,
        CancellationToken cancellationToken)
    {
        var items = new List<RealtimeSnapshotItemPayload>();
        var skipped = 0;

        foreach (var raw in rawIntervals.OrderBy(item => item.Machine, StringComparer.OrdinalIgnoreCase))
        {
            var contexts = productionContextCache.GetCapturableByMachine(raw.Machine);
            if (contexts.Count == 0)
            {
                contexts = await repository.ListCapturableProductionContextsAsync(raw.Machine, cancellationToken);
            }

            foreach (var context in contexts)
            {
                if (context.BaselineCapturedAt <= 0)
                {
                    SeedBaseline(context, raw, createdAt);
                    await repository.SaveProductionContextAsync(context, cancellationToken);
                    productionContextCache.Upsert(context);
                    logger.LogInformation(
                        "Seeded realtime OEE baseline. Machine={Machine}, OrderId={OrderId}, SessionId={SessionId}, RawId={RawId}, CapturedAt={CapturedAt}",
                        context.Machine,
                        context.OrderId,
                        context.SessionId,
                        raw.Id,
                        raw.ReadAt);
                    skipped++;
                    continue;
                }

                var product = ReadPrimaryProduct(context.ProductsJson);
                var goodQty = DeltaOrZero(raw.ShotOkTotal, context.BaselineShotOkTotal, raw.Machine, OeeSignalCodes.ShotOkCount);
                var ngQty = DeltaOrZero(raw.ShotNgTotal, context.BaselineShotNgTotal, raw.Machine, OeeSignalCodes.ShotNgCount);
                var runTime = DeltaOrZero(raw.RunTimeTotalSec, context.BaselineRunTimeTotalSec, raw.Machine, OeeSignalCodes.RunTimeTotal);
                var productionTime = Math.Max(0, createdAt - context.ActivePeriodStartAt);
                var actualQty = goodQty + ngQty;
                var cycleTimeSeconds = product.EffectiveCycleTime > 0
                    ? product.EffectiveCycleTime
                    : raw.CycleTimeMs > 0 ? raw.CycleTimeMs / 1000m : 0m;
                var plannedQty = cycleTimeSeconds > 0
                    ? Decimal.Floor(productionTime / cycleTimeSeconds) * product.Gain
                    : 0m;
                var availability = Percent(runTime, productionTime);
                var performance = plannedQty > 0 ? Percent(actualQty, plannedQty) : 0m;
                var quality = actualQty > 0 ? Percent(goodQty, actualQty) : 0m;
                var oee = Decimal.Round(availability * performance * quality / 10000m, 6);

                var item = new RealtimeSnapshotItemPayload(
                    MachineCode: raw.Machine,
                    OrderId: context.OrderId,
                    SessionId: context.SessionId,
                    ProductCode: product.ProductId,
                    MoldCode: product.MoldCode,
                    MachineState: raw.RunState,
                    ActualQty: actualQty,
                    PlannedQty: plannedQty,
                    Availability: availability,
                    Performance: performance,
                    Quality: quality,
                    Oee: oee,
                    Extra: EmptyExtra());
                items.Add(item);

                logger.LogInformation(
                    "Realtime OEE snapshot calculated. Machine={Machine}, OrderId={OrderId}, SessionId={SessionId}, Product={Product}, State={State}, ActualQty={ActualQty}, PlannedQty={PlannedQty}, A={Availability}, P={Performance}, Q={Quality}, OEE={Oee}",
                    item.MachineCode,
                    item.OrderId,
                    item.SessionId,
                    item.ProductCode,
                    item.MachineState,
                    item.ActualQty,
                    item.PlannedQty,
                    item.Availability,
                    item.Performance,
                    item.Quality,
                    item.Oee);
            }
        }

        return new RealtimeSnapshotBuildResult(
            new RealtimeSnapshotBatchPayload(1, gatewayId, createdAt, items),
            rawIntervals.Count,
            skipped);
    }

    private static void SeedBaseline(ProductionContext context, PlcRawInterval raw, long updatedAt)
    {
        context.BaselineRawId = raw.Id;
        context.BaselineCapturedAt = raw.ReadAt;
        context.BaselineShotOkTotal = raw.ShotOkTotal;
        context.BaselineShotNgTotal = raw.ShotNgTotal;
        context.BaselineRunTimeTotalSec = raw.RunTimeTotalSec;
        context.BaselineStopTimeTotalSec = raw.StopTimeTotalSec;
        context.BaselineErrorTimeTotalSec = raw.ErrorTimeTotalSec;
        context.BaselineCycleTimeMs = raw.CycleTimeMs;
        context.UpdatedAt = updatedAt;
    }

    private long DeltaOrZero(long current, long baseline, string machine, string signalCode)
    {
        var delta = current - baseline;
        if (delta >= 0)
        {
            return delta;
        }

        logger.LogWarning(
            "Realtime OEE delta was negative and was clamped to zero. Machine={Machine}, SignalCode={SignalCode}, Current={Current}, Baseline={Baseline}",
            machine,
            signalCode,
            current,
            baseline);
        return 0;
    }

    private static OeeProductDefinition ReadPrimaryProduct(string productsJson)
    {
        try
        {
            var products = JsonSerializer.Deserialize<List<OeeProductDefinition>>(productsJson, JsonOptions);
            return products?.FirstOrDefault(product => !string.IsNullOrWhiteSpace(product.ProductId)) ?? DefaultProduct();
        }
        catch (JsonException)
        {
            return DefaultProduct();
        }
    }

    private static OeeProductDefinition DefaultProduct() =>
        new()
        {
            ProductId = "UNKNOWN_PRODUCT",
            Gain = 1m,
            CycleTime = 0m
        };

    private static decimal Percent(decimal numerator, decimal denominator)
    {
        if (denominator <= 0)
        {
            return 0m;
        }

        return Decimal.Round(Math.Clamp(numerator / denominator * 100m, 0m, 100m), 6);
    }

    private static JsonElement EmptyExtra() =>
        JsonSerializer.SerializeToElement(new { }, JsonOptions);
}

public sealed class RealtimeSnapshotSyncService(
    IOptions<MesSyncOptions> options,
    IRawDataCaptureService rawDataCaptureService,
    IRealtimeSnapshotBuilder snapshotBuilder,
    IRealtimeSnapshotClient snapshotClient,
    IRealtimeSnapshotSyncStatusStore statusStore,
    ILogger<RealtimeSnapshotSyncService> logger) : IRealtimeSnapshotSyncService
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RealtimeSnapshotBuildResult> SyncAsync(string gatewayId, CancellationToken cancellationToken)
    {
        var current = options.Value;
        var interval = TimeSpan.FromMilliseconds(Math.Max(1000, current.SyncIntervalMs));
        var rawIntervals = await rawDataCaptureService.CaptureAsync(interval, current.RequireProductionContext, cancellationToken);
        var createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var build = await snapshotBuilder.BuildAsync(gatewayId, rawIntervals, createdAt, cancellationToken);
        if (build.Payload.Items.Count == 0)
        {
            logger.LogDebug(
                "No realtime snapshot items were built. RawIntervals={RawIntervals}, Skipped={Skipped}",
                build.RawIntervalCount,
                build.SkippedItemCount);
            return build;
        }

        var payloadJson = JsonSerializer.Serialize(build.Payload, PayloadJsonOptions);
        logger.LogInformation("Realtime OEE snapshot payload built. PayloadJson={PayloadJson}", payloadJson);

        try
        {
            await snapshotClient.SendAsync(build.Payload, cancellationToken);
            statusStore.MarkSuccess(createdAt, build.Payload.Items.Count);
            logger.LogInformation(
                "Realtime OEE snapshot batch sent. Gateway={Gateway}, Items={ItemCount}, CreatedAt={CreatedAt}",
                gatewayId,
                build.Payload.Items.Count,
                createdAt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            statusStore.MarkDropped(ex.Message);
            logger.LogWarning(ex, "Realtime OEE snapshot batch dropped. Items={ItemCount}", build.Payload.Items.Count);
        }

        return build;
    }
}

public sealed class RealtimeSnapshotSyncStatusStore : IRealtimeSnapshotSyncStatusStore
{
    private readonly object _lock = new();
    private RealtimeSnapshotSyncStatus _current = new(null, null, 0, 0);

    public RealtimeSnapshotSyncStatus Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public void MarkSuccess(long unixTimeSeconds, int itemCount)
    {
        lock (_lock)
        {
            _current = _current with
            {
                LastSuccessUnixTimeSeconds = unixTimeSeconds,
                LastError = null,
                LastItemCount = itemCount
            };
        }
    }

    public void MarkDropped(string error)
    {
        lock (_lock)
        {
            _current = _current with
            {
                LastError = error,
                DroppedBatchCount = _current.DroppedBatchCount + 1
            };
        }
    }
}
