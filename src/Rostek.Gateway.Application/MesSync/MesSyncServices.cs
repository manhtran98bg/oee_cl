using System.Text.Json;
using System.Text.Json.Nodes;
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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ProductionCommandResponse> HandleAsync(ProductionCommandRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.MachineCode))
        {
            return Reject(string.Empty, request.CommandCode, request.ProductionOrderCode, null, "machine_code is required.");
        }

        if (string.IsNullOrWhiteSpace(request.CommandCode))
        {
            return Reject(request.MachineCode, string.Empty, request.ProductionOrderCode, null, "command_code is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Action) || !ProductionCommandActions.TryMapStatus(request.Action, out var status))
        {
            return Reject(request.MachineCode, request.CommandCode, request.ProductionOrderCode, null, "action must be start, pause, or stop.");
        }

        var now = request.OccurredAtUnixTimeSeconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var machine = request.MachineCode.Trim();
        if (await configRepository.GetMachineByCodeAsync(machine, includeDetails: false, cancellationToken) is null)
        {
            return Reject(machine, request.CommandCode, request.ProductionOrderCode, null, $"machine_code '{machine}' was not found in Gateway configuration.");
        }

        var context = await repository.GetProductionContextAsync(machine, cancellationToken);
        var response = status switch
        {
            "active" => await StartAsync(request, context, machine, now, cancellationToken),
            "pause" => await PauseAsync(request, context, machine, now, cancellationToken),
            "stopped" => await StopAsync(request, context, machine, now, cancellationToken),
            _ => Reject(machine, request.CommandCode, request.ProductionOrderCode, null, "action must be start, pause, or stop.")
        };

        if (response.Accepted)
        {
            logger.LogInformation(
                "Production command accepted. Machine={Machine}, CommandCode={CommandCode}, Status={Status}, Order={Order}, Session={Session}",
                response.MachineCode,
                response.CommandCode,
                response.Status,
                response.ProductionOrderCode,
                response.SessionId);
        }

        return response;
    }

    private async Task<ProductionCommandResponse> StartAsync(
        ProductionCommandRequest request,
        ProductionContext? currentContext,
        string machine,
        long now,
        CancellationToken cancellationToken)
    {
        var orderCode = Normalize(request.ProductionOrderCode);
        if (orderCode is null)
        {
            return Reject(machine, request.CommandCode, request.ProductionOrderCode, null, "production_order_code is required when action is start.");
        }

        var productValidation = ValidateProducts(request.Products);
        if (productValidation is not null)
        {
            return Reject(machine, request.CommandCode, orderCode, null, productValidation);
        }

        var periodId = BuildSessionId(machine, orderCode, now);
        if (currentContext is not null &&
            currentContext.ActivePeriodId.Length > 0 &&
            !currentContext.ActivePeriodId.Equals(periodId, StringComparison.Ordinal) &&
            currentContext.Status is "active" or "pause")
        {
            await repository.CloseProductionPeriodAsync(currentContext.ActivePeriodId, now, "stopped", cancellationToken);
        }

        var existingPeriod = await repository.GetProductionPeriodAsync(periodId, cancellationToken);
        var plcPeriodIndex = existingPeriod?.PlcPeriodIndex ?? await repository.GetNextPlcPeriodIndexAsync(machine, orderCode, cancellationToken);
        var context = currentContext ?? new ProductionContext { Machine = machine };

        context.Status = "active";
        context.OrderCode = orderCode;
        context.ServerOrderId = orderCode;
        context.ActivePeriodId = periodId;
        context.ActivePeriodStartAt = now;
        context.CurrentPlcPeriodIndex = plcPeriodIndex;
        context.ProductsJson = JsonSerializer.Serialize(ToOeeProducts(request.Products!), JsonOptions);
        context.ExtraJson = BuildExtraJson(request);
        ClearBaseline(context);
        context.UpdatedAt = now;

        await repository.SaveProductionContextAsync(context, cancellationToken);
        await repository.EnsureProductionPeriodAsync(context, now, cancellationToken);
        productionContextCache.Upsert(context);

        return new ProductionCommandResponse(true, context.Machine, request.CommandCode.Trim(), context.Status, context.OrderCode, context.ActivePeriodId, "Accepted");
    }

    private async Task<ProductionCommandResponse> PauseAsync(
        ProductionCommandRequest request,
        ProductionContext? context,
        string machine,
        long now,
        CancellationToken cancellationToken)
    {
        if (context is null || context.ActivePeriodId.Length == 0)
        {
            return Reject(machine, request.CommandCode, request.ProductionOrderCode, null, "production context was not found for machine.");
        }

        context.Status = "pause";
        context.UpdatedAt = now;
        context.ExtraJson = MergeExtraJson(context.ExtraJson, request);
        await repository.SaveProductionContextAsync(context, cancellationToken);
        productionContextCache.Upsert(context);
        return new ProductionCommandResponse(true, context.Machine, request.CommandCode.Trim(), context.Status, context.OrderCode, context.ActivePeriodId, "Accepted");
    }

    private async Task<ProductionCommandResponse> StopAsync(
        ProductionCommandRequest request,
        ProductionContext? context,
        string machine,
        long now,
        CancellationToken cancellationToken)
    {
        if (context is null || context.ActivePeriodId.Length == 0)
        {
            return Reject(machine, request.CommandCode, request.ProductionOrderCode, null, "production context was not found for machine.");
        }

        context.Status = "stopped";
        context.UpdatedAt = now;
        context.ExtraJson = MergeExtraJson(context.ExtraJson, request);
        await repository.SaveProductionContextAsync(context, cancellationToken);
        await repository.CloseProductionPeriodAsync(context.ActivePeriodId, now, "stopped", cancellationToken);
        productionContextCache.Upsert(context);
        return new ProductionCommandResponse(true, context.Machine, request.CommandCode.Trim(), context.Status, context.OrderCode, context.ActivePeriodId, "Accepted");
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

    private static ProductionCommandResponse Reject(string machine, string? commandCode, string? orderCode, string? sessionId, string message) =>
        new(false, machine, Normalize(commandCode) ?? string.Empty, null, Normalize(orderCode), sessionId, message);

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

            if (product.CycleTimeSeconds <= 0)
            {
                return "products[].cycle_time_seconds must be greater than 0.";
            }
        }

        return null;
    }

    private static IReadOnlyList<OeeProductDefinition> ToOeeProducts(IReadOnlyList<ProductionCommandProduct> products) =>
        products.Select(product => new OeeProductDefinition
        {
            ProductId = product.ProductCode.Trim(),
            ProductName = Normalize(product.ProductName),
            MoldCode = Normalize(product.MoldCode),
            Gain = product.Cavity,
            CycleTime = product.CycleTimeSeconds,
            Target = product.TargetQty,
            Extra = product.Extra
        }).ToList();

    private static string BuildSessionId(string machine, string orderCode, long occurredAt) =>
        $"{Slug(machine)}-{Slug(orderCode)}-{occurredAt}";

    private static string Slug(string value)
    {
        var chars = value.Trim().Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_').ToArray();
        return new string(chars);
    }

    private static string MergeExtraJson(string existingExtraJson, ProductionCommandRequest request)
    {
        var extra = TryParseObject(existingExtraJson);
        ApplyRequestExtra(extra, request);
        return extra.ToJsonString(JsonOptions);
    }

    private static string BuildExtraJson(ProductionCommandRequest request)
    {
        var extra = TryParseObject(request.Extra);
        ApplyRequestExtra(extra, request);
        return extra.ToJsonString(JsonOptions);
    }

    private static JsonObject TryParseObject(JsonElement? element)
    {
        if (element is { ValueKind: JsonValueKind.Object } value)
        {
            return JsonNode.Parse(value.GetRawText()) as JsonObject ?? [];
        }

        return [];
    }

    private static JsonObject TryParseObject(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void ApplyRequestExtra(JsonObject extra, ProductionCommandRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.MachineName))
        {
            extra["machine_name"] = request.MachineName.Trim();
        }

        extra["schema_version"] = request.SchemaVersion;
        extra["last_command_code"] = request.CommandCode.Trim();
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
            var context = productionContextCache.Get(raw.Machine) ?? await repository.GetProductionContextAsync(raw.Machine, cancellationToken);
            if (context is null || context.ActivePeriodId.Length == 0 || context.Status.Equals("stopped", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            if (context.BaselineCapturedAt <= 0)
            {
                SeedBaseline(context, raw, createdAt);
                await repository.SaveProductionContextAsync(context, cancellationToken);
                productionContextCache.Upsert(context);
                logger.LogInformation(
                    "Seeded realtime OEE baseline. Machine={Machine}, Order={Order}, Session={Session}, RawId={RawId}, CapturedAt={CapturedAt}",
                    context.Machine,
                    context.OrderCode,
                    context.ActivePeriodId,
                    raw.Id,
                    raw.ReadAt);
                skipped++;
                continue;
            }

            var product = ReadPrimaryProduct(context.ProductsJson);
            var goodQty = DeltaOrZero(raw.ShotOkTotal, context.BaselineShotOkTotal, raw.Machine, OeeSignalCodes.ShotOkCount);
            var ngQty = DeltaOrZero(raw.ShotNgTotal, context.BaselineShotNgTotal, raw.Machine, OeeSignalCodes.ShotNgCount);
            var runTime = DeltaOrZero(raw.RunTimeTotalSec, context.BaselineRunTimeTotalSec, raw.Machine, OeeSignalCodes.RunTimeTotal);
            var stopTime = DeltaOrZero(raw.StopTimeTotalSec, context.BaselineStopTimeTotalSec, raw.Machine, OeeSignalCodes.StopTimeTotal);
            var errorTime = DeltaOrZero(raw.ErrorTimeTotalSec, context.BaselineErrorTimeTotalSec, raw.Machine, OeeSignalCodes.ErrorTimeTotal);
            var productionTime = Math.Max(0, createdAt - context.ActivePeriodStartAt);
            var actualQty = goodQty + ngQty;
            var cycleTimeSeconds = product.EffectiveCycleTime > 0
                ? product.EffectiveCycleTime
                : raw.CycleTimeMs > 0 ? raw.CycleTimeMs / 1000m : 0m;
            var plannedQty = cycleTimeSeconds > 0 ? Decimal.Round(productionTime / cycleTimeSeconds, 6) : 0m;
            var availability = Percent(runTime, productionTime);
            var performance = plannedQty > 0 ? Percent(actualQty, plannedQty) : 0m;
            var quality = actualQty > 0 ? Percent(goodQty, actualQty) : 0m;
            var oee = Decimal.Round(availability * performance * quality / 10000m, 6);

            var item = new RealtimeSnapshotItemPayload(
                Key: Guid.NewGuid().ToString("N"),
                MachineCode: raw.Machine,
                OrderCode: context.OrderCode,
                SessionId: context.ActivePeriodId,
                ProductCode: product.ProductId,
                MoldCode: product.MoldCode,
                MachineState: raw.RunState,
                GoodQty: goodQty,
                NgQty: ngQty,
                ActualQty: actualQty,
                PlannedQty: plannedQty,
                RunTime: runTime,
                StopTime: stopTime,
                ErrorTime: errorTime,
                ProductionTime: productionTime,
                Availability: availability,
                Performance: performance,
                Quality: quality,
                Oee: oee,
                Extra: EmptyExtra());
            items.Add(item);

            logger.LogInformation(
                "Realtime OEE snapshot calculated. Machine={Machine}, Order={Order}, Session={Session}, Product={Product}, State={State}, GoodQty={GoodQty}, NgQty={NgQty}, ActualQty={ActualQty}, PlannedQty={PlannedQty}, RunTime={RunTime}, StopTime={StopTime}, ErrorTime={ErrorTime}, ProductionTime={ProductionTime}, A={Availability}, P={Performance}, Q={Quality}, OEE={Oee}",
                item.MachineCode,
                item.OrderCode,
                item.SessionId,
                item.ProductCode,
                item.MachineState,
                item.GoodQty,
                item.NgQty,
                item.ActualQty,
                item.PlannedQty,
                item.RunTime,
                item.StopTime,
                item.ErrorTime,
                item.ProductionTime,
                item.Availability,
                item.Performance,
                item.Quality,
                item.Oee);
        }

        return new RealtimeSnapshotBuildResult(
            new RealtimeSnapshotBatchPayload(1, gatewayId, createdAt, items),
            rawIntervals.Count,
            skipped);
    }

    private void SeedBaseline(ProductionContext context, PlcRawInterval raw, long updatedAt)
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
