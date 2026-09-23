using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Contracts.Runtime;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.MesSync;

public sealed class ProductionCommandService(
    IOeeLocalRepository repository,
    IProductionContextCache productionContextCache,
    IOrderQuantityCache orderQuantityCache,
    IProductionMetricBuilder productionMetricBuilder,
    IOptions<MesSyncOptions> options,
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
            items.Add(await HandleItemAsync(item, request.GatewayId, now, cancellationToken));
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
        return await HandleItemAsync(item, "LEGACY", now, cancellationToken);
    }

    private async Task<ProductionCommandResponse> HandleItemAsync(
        ProductionCommandItemRequest request,
        string gatewayId,
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
            "pause" => await PauseAsync(request, gatewayId, machine, orderId, now, cancellationToken),
            "stopped" => await StopAsync(request, gatewayId, machine, orderId, now, cancellationToken),
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
        string gatewayId,
        string machine,
        string orderId,
        long now,
        CancellationToken cancellationToken) =>
        await EndSessionAsync(
            request,
            gatewayId,
            machine,
            orderId,
            now,
            periodStatus: "paused",
            responseStatus: "pause",
            cancellationToken);

    private async Task<ProductionCommandResponse> StopAsync(
        ProductionCommandItemRequest request,
        string gatewayId,
        string machine,
        string orderId,
        long now,
        CancellationToken cancellationToken) =>
        await EndSessionAsync(
            request,
            gatewayId,
            machine,
            orderId,
            now,
            periodStatus: "stopped",
            responseStatus: "stopped",
            cancellationToken);

    private async Task<ProductionCommandResponse> EndSessionAsync(
        ProductionCommandItemRequest request,
        string gatewayId,
        string machine,
        string orderId,
        long endedAt,
        string periodStatus,
        string responseStatus,
        CancellationToken cancellationToken)
    {
        var context = await repository.GetActiveProductionContextAsync(machine, orderId, cancellationToken);
        if (context is null)
        {
            return Reject(machine, request.CommandCode, orderId, null, "active production context was not found for machine/order.");
        }

        await TryFinalizeSessionMetricAsync(gatewayId, context, endedAt, cancellationToken);
        await CloseOpenMachineStateEventAsync(context, endedAt, cancellationToken);
        await repository.CloseProductionPeriodAsync(context.SessionId, endedAt, periodStatus, cancellationToken);
        await repository.DeleteProductionContextAsync(context.SessionId, cancellationToken);
        productionContextCache.Remove(context.SessionId);
        return new ProductionCommandResponse(true, context.Machine, request.CommandCode.Trim(), responseStatus, context.OrderId, context.SessionId, "Accepted");
    }

    private async Task TryFinalizeSessionMetricAsync(
        string gatewayId,
        ProductionContext context,
        long endedAt,
        CancellationToken cancellationToken)
    {
        var metric = await productionMetricBuilder.BuildFinalSessionAsync(gatewayId, context, endedAt, cancellationToken);
        if (metric is null)
        {
            return;
        }

        metric.TotalQty = orderQuantityCache.GetCompletedQty(context.Machine, context.OrderId) + metric.ActualQty;
        await repository.UpsertProductionMetricsAsync([metric], cancellationToken);

        if (options.Value.ProductionMetricsEnabled)
        {
            await EnqueueProductionMetricAsync(metric, endedAt, cancellationToken);
        }

        var added = orderQuantityCache.AddCompletedSession(context.Machine, context.OrderId, context.SessionId, metric.ActualQty);
        logger.LogInformation(
            "Final session metric saved. Machine={Machine}, OrderId={OrderId}, SessionId={SessionId}, ActualQty={ActualQty}, TotalQty={TotalQty}, AddedToOrderCache={AddedToOrderCache}",
            context.Machine,
            context.OrderId,
            context.SessionId,
            metric.ActualQty,
            metric.TotalQty,
            added);
    }

    private async Task EnqueueProductionMetricAsync(
        ProductionMetric metric,
        long createdAt,
        CancellationToken cancellationToken)
    {
        await repository.UpsertSyncOutboxMessageAsync(new SyncOutboxMessage
        {
            Topic = SyncOutboxTopics.ProductionMetric,
            DedupeKey = $"production_metric:{metric.MetricId}",
            EndpointPath = MesSyncEndpointPaths.ProductionMetrics,
            PayloadJson = JsonSerializer.Serialize(ToPayloadItem(metric), JsonOptions),
            Status = SyncOutboxStatuses.Pending,
            AttemptCount = 0,
            NextAttemptAt = createdAt,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        }, cancellationToken);
    }

    private static ProductionMetricItemPayload ToPayloadItem(ProductionMetric metric) =>
        new(
            metric.MetricId,
            metric.BucketType,
            metric.BucketStart,
            metric.BucketEnd,
            metric.Machine,
            metric.OrderId,
            metric.SessionId,
            metric.ProductCode,
            metric.MoldCode,
            metric.MachineState,
            metric.ActualQty,
            metric.TotalQty,
            metric.PlannedQty,
            metric.TargetQty,
            metric.RunTime,
            metric.StopTime,
            metric.ErrorTime,
            metric.ProductionTime,
            metric.Availability,
            metric.Performance,
            metric.Quality,
            metric.Oee,
            metric.IsFinal,
            JsonSerializer.Deserialize<JsonElement>(metric.ExtraJson));

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

    private async Task CloseOpenMachineStateEventAsync(ProductionContext context, long endAt, CancellationToken cancellationToken)
    {
        var openEvent = await repository.GetOpenMachineStateEventAsync(
            context.Machine,
            context.OrderId,
            context.SessionId,
            cancellationToken);
        if (openEvent is null)
        {
            return;
        }

        openEvent.IsOpen = false;
        openEvent.EndAt = Math.Max(openEvent.StartAt, endAt);
        openEvent.DurationSec = Math.Max(0, openEvent.EndAt - openEvent.StartAt);
        openEvent.UpdatedAt = endAt;
        await repository.SaveMachineStateEventsAsync([openEvent], cancellationToken);
        var payload = new MachineStateEventItemPayload(
            openEvent.EventId,
            openEvent.Machine,
            openEvent.OrderId,
            openEvent.SessionId,
            openEvent.State,
            openEvent.StartAt,
            openEvent.EndAt,
            openEvent.DurationSec,
            openEvent.IsOpen);
        await repository.UpsertSyncOutboxMessageAsync(new SyncOutboxMessage
        {
            Topic = SyncOutboxTopics.MachineStateEvent,
            DedupeKey = $"machine_state_event:{openEvent.EventId}",
            EndpointPath = MesSyncEndpointPaths.MachineStateEvents,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            Status = SyncOutboxStatuses.Pending,
            NextAttemptAt = endAt,
            CreatedAt = endAt,
            UpdatedAt = endAt
        }, cancellationToken);
    }

    private static string Slug(string value)
    {
        var chars = value.Trim().Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_').ToArray();
        return new string(chars);
    }
}

public sealed class RealtimeSnapshotBuilder(
    IOeeLocalRepository repository,
    IProductionContextCache productionContextCache,
    IOrderQuantityCache orderQuantityCache,
    IRuntimeConfigurationProvider runtimeConfigurationProvider) : IRealtimeSnapshotBuilder
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

        var currentRawByMachine = rawIntervals
            .GroupBy(raw => raw.Machine, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(raw => raw.ReadAt).First(),
                StringComparer.OrdinalIgnoreCase);
        var enabledMachines = runtimeConfigurationProvider.Current.Machines.Values
            .Where(machine => machine.Enabled)
            .OrderBy(machine => machine.MachineCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var machine in enabledMachines)
        {
            var hasCurrentRaw = currentRawByMachine.TryGetValue(machine.MachineCode, out var currentRaw);
            var contexts = productionContextCache.GetCapturableByMachine(machine.MachineCode);
            if (contexts.Count == 0)
            {
                contexts = await repository.ListCapturableProductionContextsAsync(machine.MachineCode, cancellationToken);
            }

            if (contexts.Count == 0)
            {
                items.Add(CreateNoContextItem(
                    machine.MachineCode,
                    hasCurrentRaw ? currentRaw!.RunState : OeeRunStates.Disconnect));
                continue;
            }

            var effectiveRaw = currentRaw;
            if (effectiveRaw is null)
            {
                effectiveRaw = await repository.GetLatestRawIntervalAsync(machine.MachineCode, cancellationToken);
            }

            foreach (var context in contexts)
            {
                var state = hasCurrentRaw ? effectiveRaw!.RunState : OeeRunStates.Disconnect;
                if (effectiveRaw is null || effectiveRaw.ReadAt < context.ActivePeriodStartAt)
                {
                    items.Add(CreateEmptyContextItem(context, state));
                    skipped++;
                    continue;
                }

                if (context.BaselineCapturedAt <= 0)
                {
                    SeedBaseline(context, effectiveRaw, createdAt);
                    await repository.SaveProductionContextAsync(context, cancellationToken);
                    productionContextCache.Upsert(context);
                    items.Add(CreateEmptyContextItem(context, state));
                    skipped++;
                    continue;
                }

                var metricAt = hasCurrentRaw ? createdAt : effectiveRaw.ReadAt;
                var item = CreateContextItem(context, effectiveRaw, metricAt, state);
                items.Add(item);
            }
        }

        return new RealtimeSnapshotBuildResult(
            new RealtimeSnapshotBatchPayload(2, gatewayId, createdAt, items),
            rawIntervals.Count,
            skipped);
    }

    private RealtimeSnapshotItemPayload CreateContextItem(
        ProductionContext context,
        PlcRawInterval raw,
        long metricAt,
        string state)
    {
        var product = ReadPrimaryProduct(context.ProductsJson);
        var goodQty = DeltaOrZero(raw.ShotOkTotal, context.BaselineShotOkTotal);
        var ngQty = DeltaOrZero(raw.ShotNgTotal, context.BaselineShotNgTotal);
        var runTime = DeltaOrZero(raw.RunTimeTotalSec, context.BaselineRunTimeTotalSec);
        var productionTime = Math.Max(0, metricAt - context.ActivePeriodStartAt);
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
        var totalQty = orderQuantityCache.GetCompletedQty(raw.Machine, context.OrderId) + actualQty;

        return new RealtimeSnapshotItemPayload(
            raw.Machine,
            context.OrderId,
            context.SessionId,
            product.ProductId,
            product.MoldCode,
            state,
            actualQty,
            totalQty,
            plannedQty,
            availability,
            performance,
            quality,
            oee,
            EmptyExtra());
    }

    private RealtimeSnapshotItemPayload CreateEmptyContextItem(ProductionContext context, string state)
    {
        var product = ReadPrimaryProduct(context.ProductsJson);
        return new RealtimeSnapshotItemPayload(
            context.Machine,
            context.OrderId,
            context.SessionId,
            product.ProductId,
            product.MoldCode,
            state,
            0,
            orderQuantityCache.GetCompletedQty(context.Machine, context.OrderId),
            0,
            0,
            0,
            0,
            0,
            EmptyExtra());
    }

    private static RealtimeSnapshotItemPayload CreateNoContextItem(string machineCode, string state) =>
        new(
            machineCode,
            null,
            null,
            null,
            null,
            state,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            EmptyExtra());

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

    private static long DeltaOrZero(long current, long baseline) => Math.Max(0, current - baseline);

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

public sealed class MachineStateEventBuilder(
    IOeeLocalRepository repository,
    IProductionContextCache productionContextCache,
    IOptions<MesSyncOptions> options,
    ILogger<MachineStateEventBuilder> logger) : IMachineStateEventBuilder
{
    public async Task<MachineStateEventBuildResult> BuildAsync(
        string gatewayId,
        IReadOnlyCollection<PlcRawInterval> rawIntervals,
        long createdAt,
        CancellationToken cancellationToken)
    {
        var changed = new List<MachineStateEvent>();
        var skipped = 0;
        var gapThresholdSeconds = Math.Max(1, options.Value.MachineStateEventGapThresholdMs / 1000);

        foreach (var raw in rawIntervals.OrderBy(item => item.ReadAt).ThenBy(item => item.Machine, StringComparer.OrdinalIgnoreCase))
        {
            var contexts = productionContextCache.GetCapturableByMachine(raw.Machine);
            if (contexts.Count == 0)
            {
                contexts = await repository.ListCapturableProductionContextsAsync(raw.Machine, cancellationToken);
            }

            if (contexts.Count == 0)
            {
                skipped++;
                continue;
            }

            foreach (var context in contexts)
            {
                var openEvent = await repository.GetOpenMachineStateEventAsync(
                    context.Machine,
                    context.OrderId,
                    context.SessionId,
                    cancellationToken);

                if (openEvent is null)
                {
                    changed.Add(CreateEvent(gatewayId, context, raw.RunState, raw.ReadAt, raw.ReadAt, isOpen: true, createdAt));
                    continue;
                }

                if (raw.ReadAt <= openEvent.EndAt)
                {
                    skipped++;
                    continue;
                }

                var gap = raw.ReadAt - openEvent.EndAt;
                if (gap > gapThresholdSeconds)
                {
                    openEvent.IsOpen = false;
                    openEvent.DurationSec = Math.Max(0, openEvent.EndAt - openEvent.StartAt);
                    openEvent.UpdatedAt = createdAt;
                    changed.Add(openEvent);

                    changed.Add(CreateEvent(gatewayId, context, OeeRunStates.Disconnect, openEvent.EndAt, raw.ReadAt, isOpen: false, createdAt));
                    changed.Add(CreateEvent(gatewayId, context, raw.RunState, raw.ReadAt, raw.ReadAt, isOpen: true, createdAt));

                    logger.LogInformation(
                        "Machine state event gap detected. Machine={Machine}, OrderId={OrderId}, SessionId={SessionId}, PreviousEndAt={PreviousEndAt}, RawReadAt={RawReadAt}, GapSeconds={GapSeconds}",
                        context.Machine,
                        context.OrderId,
                        context.SessionId,
                        openEvent.EndAt,
                        raw.ReadAt,
                        gap);
                    continue;
                }

                if (openEvent.State.Equals(raw.RunState, StringComparison.OrdinalIgnoreCase))
                {
                    openEvent.EndAt = raw.ReadAt;
                    openEvent.DurationSec = Math.Max(0, openEvent.EndAt - openEvent.StartAt);
                    openEvent.UpdatedAt = createdAt;
                    changed.Add(openEvent);
                    continue;
                }

                openEvent.IsOpen = false;
                openEvent.EndAt = raw.ReadAt;
                openEvent.DurationSec = Math.Max(0, openEvent.EndAt - openEvent.StartAt);
                openEvent.UpdatedAt = createdAt;
                changed.Add(openEvent);
                changed.Add(CreateEvent(gatewayId, context, raw.RunState, raw.ReadAt, raw.ReadAt, isOpen: true, createdAt));
            }
        }

        await repository.SaveMachineStateEventsAsync(changed, cancellationToken);
        return new MachineStateEventBuildResult(changed, skipped);
    }

    private static MachineStateEvent CreateEvent(
        string gatewayId,
        ProductionContext context,
        string state,
        long startAt,
        long endAt,
        bool isOpen,
        long createdAt) =>
        new()
        {
            EventId = BuildEventId(gatewayId, context.Machine, context.SessionId, startAt, state),
            GatewayId = gatewayId,
            Machine = context.Machine,
            OrderId = context.OrderId,
            SessionId = context.SessionId,
            State = state,
            StartAt = startAt,
            EndAt = endAt,
            DurationSec = Math.Max(0, endAt - startAt),
            IsOpen = isOpen,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };

    private static string BuildEventId(string gatewayId, string machine, string sessionId, long startAt, string state) =>
        $"{gatewayId}:{machine}:{sessionId}:{startAt}:{state}";
}

public sealed class OeeLocalProcessingService(
    IOptions<MesSyncOptions> options,
    IOeeLocalRepository repository,
    IRealtimeSnapshotBuilder snapshotBuilder,
    IMachineStateEventBuilder stateEventBuilder,
    IProductionMetricBuilder productionMetricBuilder,
    ILogger<OeeLocalProcessingService> logger) : IOeeLocalProcessingService
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<OeeLocalProcessingResult> ProcessAsync(
        string gatewayId,
        IReadOnlyCollection<PlcRawInterval> rawIntervals,
        long createdAt,
        CancellationToken cancellationToken)
    {
        var stateEvents = await stateEventBuilder.BuildAsync(gatewayId, rawIntervals, createdAt, cancellationToken);
        var snapshot = await snapshotBuilder.BuildAsync(gatewayId, rawIntervals, createdAt, cancellationToken);
        var productionMetrics = await productionMetricBuilder.BuildAsync(gatewayId, rawIntervals, createdAt, cancellationToken);
        var enqueued = 0;
        var current = options.Value;

        if (current.MachineStateEventsEnabled)
        {
            foreach (var stateEvent in stateEvents.Events)
            {
                await EnqueueAsync(
                    SyncOutboxTopics.MachineStateEvent,
                    $"machine_state_event:{stateEvent.EventId}",
                    MesSyncEndpointPaths.MachineStateEvents,
                    ToPayloadItem(stateEvent),
                    createdAt,
                    cancellationToken);
                enqueued++;
            }
        }

        if (current.RealtimeSnapshotsEnabled)
        {
            var retainedDedupeKeys = new List<string>(snapshot.Payload.Items.Count);
            foreach (var item in snapshot.Payload.Items)
            {
                var dedupeKey = BuildRealtimeSnapshotDedupeKey(item);
                await EnqueueAsync(
                    SyncOutboxTopics.RealtimeSnapshot,
                    dedupeKey,
                    MesSyncEndpointPaths.RealtimeSnapshots,
                    item,
                    createdAt,
                    cancellationToken);
                retainedDedupeKeys.Add(dedupeKey);
                enqueued++;
            }

            await repository.RemoveStaleRealtimeSnapshotMessagesAsync(retainedDedupeKeys, cancellationToken);
        }

        if (current.ProductionMetricsEnabled)
        {
            foreach (var metric in productionMetrics.Metrics)
            {
                await EnqueueAsync(
                    SyncOutboxTopics.ProductionMetric,
                    $"production_metric:{metric.MetricId}",
                    MesSyncEndpointPaths.ProductionMetrics,
                    ToPayloadItem(metric),
                    createdAt,
                    cancellationToken);
                enqueued++;
            }
        }

        logger.LogDebug(
            "OEE local processing completed. RawIntervals={RawIntervals}, StateEvents={StateEvents}, SnapshotItems={SnapshotItems}, ProductionMetrics={ProductionMetrics}, Enqueued={Enqueued}",
            rawIntervals.Count,
            stateEvents.Events.Count,
            snapshot.Payload.Items.Count,
            productionMetrics.Metrics.Count,
            enqueued);

        return new OeeLocalProcessingResult(snapshot, stateEvents, productionMetrics, enqueued);
    }

    private async Task EnqueueAsync<TPayload>(
        string topic,
        string dedupeKey,
        string endpointPath,
        TPayload payload,
        long createdAt,
        CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(payload, PayloadJsonOptions);
        await repository.UpsertSyncOutboxMessageAsync(new SyncOutboxMessage
        {
            Topic = topic,
            DedupeKey = dedupeKey,
            EndpointPath = endpointPath,
            PayloadJson = payloadJson,
            Status = SyncOutboxStatuses.Pending,
            AttemptCount = 0,
            NextAttemptAt = createdAt,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        }, cancellationToken);
    }

    private static string BuildRealtimeSnapshotDedupeKey(RealtimeSnapshotItemPayload item) =>
        string.IsNullOrWhiteSpace(item.SessionId)
            ? $"realtime_snapshot:{item.MachineCode}:no-context"
            : $"realtime_snapshot:{item.MachineCode}:{item.OrderId}:{item.SessionId}";

    private static MachineStateEventItemPayload ToPayloadItem(MachineStateEvent stateEvent) =>
        new(
            stateEvent.EventId,
            stateEvent.Machine,
            stateEvent.OrderId,
            stateEvent.SessionId,
            stateEvent.State,
            stateEvent.StartAt,
            stateEvent.EndAt,
            stateEvent.DurationSec,
            stateEvent.IsOpen);

    private static ProductionMetricItemPayload ToPayloadItem(ProductionMetric metric) =>
        new(
            metric.MetricId,
            metric.BucketType,
            metric.BucketStart,
            metric.BucketEnd,
            metric.Machine,
            metric.OrderId,
            metric.SessionId,
            metric.ProductCode,
            metric.MoldCode,
            metric.MachineState,
            metric.ActualQty,
            metric.TotalQty,
            metric.PlannedQty,
            metric.TargetQty,
            metric.RunTime,
            metric.StopTime,
            metric.ErrorTime,
            metric.ProductionTime,
            metric.Availability,
            metric.Performance,
            metric.Quality,
            metric.Oee,
            metric.IsFinal,
            JsonSerializer.Deserialize<JsonElement>(metric.ExtraJson));
}

public sealed class ProductionMetricBuilder(
    IOeeLocalRepository repository,
    IProductionContextCache productionContextCache,
    IOptions<MesSyncOptions> options,
    ILogger<ProductionMetricBuilder> logger) : IProductionMetricBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ProductionMetricBuildResult> BuildAsync(
        string gatewayId,
        IReadOnlyCollection<PlcRawInterval> rawIntervals,
        long createdAt,
        CancellationToken cancellationToken)
    {
        var metrics = new List<ProductionMetric>();
        var skipped = 0;
        var timeZone = ResolveTimeZone(options.Value.ProductionMetricTimeZoneId);

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
                    skipped++;
                    continue;
                }

                var product = ReadPrimaryProduct(context.ProductsJson);
                var sessionMetric = TryBuildMetric(
                    gatewayId,
                    "session",
                    context.ActivePeriodStartAt,
                    createdAt,
                    isFinal: false,
                    context,
                    product,
                    raw,
                    context.BaselineShotOkTotal,
                    context.BaselineShotNgTotal,
                    context.BaselineRunTimeTotalSec,
                    context.BaselineStopTimeTotalSec,
                    context.BaselineErrorTimeTotalSec,
                    createdAt);
                if (sessionMetric is not null)
                {
                    metrics.Add(sessionMetric);
                }

                var orderMetric = sessionMetric is null
                    ? null
                    : CloneAsOrderMetric(gatewayId, sessionMetric, context, createdAt);
                if (orderMetric is not null)
                {
                    metrics.Add(orderMetric);
                }

                var hour = GetLocalBucket(createdAt, timeZone, TimeSpan.FromHours(1));
                var previousHourMetric = await TryBuildBucketMetricAsync(gatewayId, "hour", hour.Start - 3600, hour.Start, context, product, createdAt, cancellationToken);
                if (previousHourMetric is not null)
                {
                    metrics.Add(previousHourMetric);
                }

                var hourMetric = await TryBuildBucketMetricAsync(gatewayId, "hour", hour.Start, hour.End, context, product, createdAt, cancellationToken);
                if (hourMetric is not null)
                {
                    metrics.Add(hourMetric);
                }

                var day = GetLocalDayBucket(createdAt, timeZone);
                var previousDayMetric = await TryBuildBucketMetricAsync(gatewayId, "day", day.Start - 86400, day.Start, context, product, createdAt, cancellationToken);
                if (previousDayMetric is not null)
                {
                    metrics.Add(previousDayMetric);
                }

                var dayMetric = await TryBuildBucketMetricAsync(gatewayId, "day", day.Start, day.End, context, product, createdAt, cancellationToken);
                if (dayMetric is not null)
                {
                    metrics.Add(dayMetric);
                }
            }
        }

        await repository.UpsertProductionMetricsAsync(metrics, cancellationToken);
        logger.LogDebug(
            "Production metrics built. Metrics={MetricCount}, Skipped={SkippedCount}",
            metrics.Count,
            skipped);
        return new ProductionMetricBuildResult(metrics, skipped);
    }

    public async Task<ProductionMetric?> BuildFinalSessionAsync(
        string gatewayId,
        ProductionContext context,
        long stoppedAt,
        CancellationToken cancellationToken)
    {
        if (context.BaselineCapturedAt <= 0)
        {
            logger.LogWarning(
                "Cannot build final session metric because baseline has not been seeded. Machine={Machine}, OrderId={OrderId}, SessionId={SessionId}",
                context.Machine,
                context.OrderId,
                context.SessionId);
            return null;
        }

        var raw = await repository.GetLatestRawIntervalAsync(context.Machine, cancellationToken);
        if (raw is null)
        {
            logger.LogWarning(
                "Cannot build final session metric because latest raw interval was not found. Machine={Machine}, OrderId={OrderId}, SessionId={SessionId}",
                context.Machine,
                context.OrderId,
                context.SessionId);
            return null;
        }

        var product = ReadPrimaryProduct(context.ProductsJson);
        var metric = TryBuildMetric(
            gatewayId,
            "session",
            context.ActivePeriodStartAt,
            stoppedAt,
            isFinal: true,
            context,
            product,
            raw,
            context.BaselineShotOkTotal,
            context.BaselineShotNgTotal,
            context.BaselineRunTimeTotalSec,
            context.BaselineStopTimeTotalSec,
            context.BaselineErrorTimeTotalSec,
            stoppedAt);

        if (metric is null)
        {
            logger.LogWarning(
                "Cannot build final session metric because calculated deltas were invalid. Machine={Machine}, OrderId={OrderId}, SessionId={SessionId}, RawReadAt={RawReadAt}",
                context.Machine,
                context.OrderId,
                context.SessionId,
                raw.ReadAt);
        }

        return metric;
    }

    private async Task<ProductionMetric?> TryBuildBucketMetricAsync(
        string gatewayId,
        string bucketType,
        long bucketStart,
        long bucketEnd,
        ProductionContext context,
        OeeProductDefinition product,
        long createdAt,
        CancellationToken cancellationToken)
    {
        var effectiveStart = Math.Max(context.ActivePeriodStartAt, bucketStart);
        var effectiveEnd = Math.Min(createdAt, bucketEnd);
        if (effectiveEnd <= effectiveStart)
        {
            return null;
        }

        var rawItems = await repository.ListRawIntervalsAsync(context.Machine, effectiveStart, effectiveEnd, cancellationToken);
        if (rawItems.Count == 0)
        {
            return null;
        }

        var baseline = rawItems.First();
        var current = rawItems.Last();
        if (current.ReadAt < baseline.ReadAt)
        {
            return null;
        }

        return TryBuildMetric(
            gatewayId,
            bucketType,
            bucketStart,
            bucketEnd,
            isFinal: createdAt >= bucketEnd,
            context,
            product,
            current,
            baseline.ShotOkTotal,
            baseline.ShotNgTotal,
            baseline.RunTimeTotalSec,
            baseline.StopTimeTotalSec,
            baseline.ErrorTimeTotalSec,
            createdAt,
            productionTimeOverride: Math.Max(0, effectiveEnd - effectiveStart));
    }

    private static ProductionMetric? TryBuildMetric(
        string gatewayId,
        string bucketType,
        long bucketStart,
        long bucketEnd,
        bool isFinal,
        ProductionContext context,
        OeeProductDefinition product,
        PlcRawInterval currentRaw,
        long baselineShotOk,
        long baselineShotNg,
        long baselineRunTime,
        long baselineStopTime,
        long baselineErrorTime,
        long createdAt,
        long? productionTimeOverride = null)
    {
        var goodQty = currentRaw.ShotOkTotal - baselineShotOk;
        var ngQty = currentRaw.ShotNgTotal - baselineShotNg;
        var runTime = currentRaw.RunTimeTotalSec - baselineRunTime;
        var stopTime = currentRaw.StopTimeTotalSec - baselineStopTime;
        var errorTime = currentRaw.ErrorTimeTotalSec - baselineErrorTime;
        if (goodQty < 0 || ngQty < 0 || runTime < 0 || stopTime < 0 || errorTime < 0)
        {
            return null;
        }

        var actualQty = goodQty + ngQty;
        var productionTime = productionTimeOverride ?? Math.Max(0, bucketEnd - bucketStart);
        var cycleTime = product.EffectiveCycleTime > 0
            ? product.EffectiveCycleTime
            : currentRaw.CycleTimeMs > 0 ? currentRaw.CycleTimeMs / 1000m : 0m;
        var plannedQty = cycleTime > 0
            ? decimal.Floor(productionTime / cycleTime) * product.Gain
            : 0m;
        var availability = Percent(runTime, productionTime);
        var performance = plannedQty > 0 ? Percent(actualQty, plannedQty) : 0m;
        var quality = actualQty > 0 ? Percent(goodQty, actualQty) : 0m;
        var oee = decimal.Round(availability * performance * quality / 10000m, 6);
        var metricId = BuildMetricId(gatewayId, bucketType, context.Machine, context.OrderId, context.SessionId, bucketStart);

        return new ProductionMetric
        {
            MetricId = metricId,
            BucketType = bucketType,
            BucketStart = bucketStart,
            BucketEnd = bucketEnd,
            IsFinal = isFinal,
            GatewayId = gatewayId,
            Machine = context.Machine,
            OrderId = context.OrderId,
            SessionId = bucketType == "order" ? null : context.SessionId,
            ProductCode = string.IsNullOrWhiteSpace(product.ProductId) ? "UNKNOWN_PRODUCT" : product.ProductId,
            MoldCode = product.MoldCode,
            MachineState = currentRaw.RunState,
            ActualQty = actualQty,
            TotalQty = actualQty,
            PlannedQty = plannedQty,
            TargetQty = product.Target,
            RunTime = runTime,
            StopTime = stopTime,
            ErrorTime = errorTime,
            ProductionTime = productionTime,
            Availability = availability,
            Performance = performance,
            Quality = quality,
            Oee = oee,
            ExtraJson = "{}",
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    private static ProductionMetric CloneAsOrderMetric(string gatewayId, ProductionMetric source, ProductionContext context, long createdAt)
    {
        var orderStart = context.ActivePeriodStartAt;
        return new ProductionMetric
        {
            MetricId = BuildMetricId(gatewayId, "order", context.Machine, context.OrderId, "order", orderStart),
            BucketType = "order",
            BucketStart = orderStart,
            BucketEnd = createdAt,
            IsFinal = false,
            GatewayId = gatewayId,
            Machine = source.Machine,
            OrderId = source.OrderId,
            SessionId = null,
            ProductCode = source.ProductCode,
            MoldCode = source.MoldCode,
            MachineState = source.MachineState,
            ActualQty = source.ActualQty,
            TotalQty = source.TotalQty,
            PlannedQty = source.PlannedQty,
            TargetQty = source.TargetQty,
            RunTime = source.RunTime,
            StopTime = source.StopTime,
            ErrorTime = source.ErrorTime,
            ProductionTime = source.ProductionTime,
            Availability = source.Availability,
            Performance = source.Performance,
            Quality = source.Quality,
            Oee = source.Oee,
            ExtraJson = "{}",
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    private static OeeProductDefinition ReadPrimaryProduct(string productsJson)
    {
        try
        {
            var products = JsonSerializer.Deserialize<List<OeeProductDefinition>>(productsJson, JsonOptions);
            return products?.FirstOrDefault(product => !string.IsNullOrWhiteSpace(product.ProductId)) ?? new OeeProductDefinition { ProductId = "UNKNOWN_PRODUCT", Gain = 1m };
        }
        catch (JsonException)
        {
            return new OeeProductDefinition { ProductId = "UNKNOWN_PRODUCT", Gain = 1m };
        }
    }

    private static decimal Percent(decimal numerator, decimal denominator)
    {
        if (denominator <= 0)
        {
            return 0m;
        }

        return decimal.Round(Math.Clamp(numerator / denominator * 100m, 0m, 100m), 6);
    }

    private static string BuildMetricId(string gatewayId, string bucketType, string machine, string orderId, string sessionId, long bucketStart) =>
        $"{gatewayId}:{bucketType}:{machine}:{orderId}:{sessionId}:{bucketStart}";

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
    }

    private static (long Start, long End) GetLocalBucket(long unixSeconds, TimeZoneInfo timeZone, TimeSpan size)
    {
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(unixSeconds), timeZone);
        var hourStart = new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Offset);
        var hourEnd = hourStart.Add(size);
        return (hourStart.ToUnixTimeSeconds(), hourEnd.ToUnixTimeSeconds());
    }

    private static (long Start, long End) GetLocalDayBucket(long unixSeconds, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(unixSeconds), timeZone);
        var dayStart = new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, local.Offset);
        var dayEnd = dayStart.AddDays(1);
        return (dayStart.ToUnixTimeSeconds(), dayEnd.ToUnixTimeSeconds());
    }
}

public sealed class MesSyncOutboxDispatcher(
    IOptions<MesSyncOptions> options,
    IOeeLocalRepository repository,
    ISyncOutboxHttpClient httpClient,
    ILogger<MesSyncOutboxDispatcher> logger) : ISyncOutboxDispatcher
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SyncOutboxDispatchResult> DispatchPendingAsync(
        string gatewayId,
        IReadOnlyCollection<string> topics,
        CancellationToken cancellationToken)
    {
        var current = options.Value;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var messages = await repository.TakePendingSyncOutboxMessagesAsync(now, current.BatchSize, topics, cancellationToken);
        var sent = 0;
        var failed = 0;

        foreach (var group in messages.GroupBy(message => message.EndpointPath, StringComparer.OrdinalIgnoreCase))
        {
            var ids = group.Select(message => message.Id).ToArray();
            try
            {
                var payload = BuildBatchPayload(gatewayId, now, group);
                await httpClient.SendAsync(group.Key, payload, cancellationToken);
                await repository.MarkSyncOutboxMessagesSucceededAsync(ids, now, cancellationToken);
                sent += ids.Length;
                logger.LogInformation(
                    "MES sync outbox batch sent. Endpoint={Endpoint}, Items={ItemCount}",
                    group.Key,
                    ids.Length);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var nextAttemptAt = now + CalculateRetryDelaySeconds(group.Max(message => message.AttemptCount), current.RetryCount);
                await repository.MarkSyncOutboxMessagesFailedAsync(ids, ex.Message, nextAttemptAt, cancellationToken);
                failed += ids.Length;
                logger.LogWarning(
                    null,
                    "MES sync outbox batch failed. Endpoint={Endpoint}, Items={ItemCount}, NextAttemptAt={NextAttemptAt}",
                    group.Key,
                    ids.Length,
                    nextAttemptAt);
            }
        }

        return new SyncOutboxDispatchResult(sent, failed);
    }

    private static string BuildBatchPayload(string gatewayId, long createdAt, IEnumerable<SyncOutboxMessage> messages)
    {
        var messageList = messages.ToList();
        var items = new JsonArray();
        foreach (var message in messageList)
        {
            var node = JsonNode.Parse(message.PayloadJson);
            if (node is not null)
            {
                items.Add(node);
            }
        }

        var payload = new JsonObject
        {
            ["schema_version"] = messageList.Any(message =>
                message.Topic.Equals(SyncOutboxTopics.RealtimeSnapshot, StringComparison.OrdinalIgnoreCase)) ? 2 : 1,
            ["gateway_id"] = gatewayId,
            ["created_at"] = createdAt,
            ["items"] = items
        };
        return payload.ToJsonString(PayloadJsonOptions);
    }

    private static long CalculateRetryDelaySeconds(int currentAttemptCount, int retryCount)
    {
        var attempt = Math.Max(1, currentAttemptCount + 1);
        var cappedAttempt = Math.Min(attempt, Math.Max(1, retryCount));
        return Math.Min(300, (long)Math.Pow(2, cappedAttempt));
    }
}

public sealed class RealtimeSnapshotSyncService(
    IOptions<MesSyncOptions> options,
    IRawDataCaptureService rawDataCaptureService,
    IOeeLocalProcessingService localProcessingService,
    ISyncOutboxDispatcher outboxDispatcher,
    IRealtimeSnapshotSyncStatusStore statusStore) : IRealtimeSnapshotSyncService
{
    public async Task<RealtimeSnapshotBuildResult> SyncAsync(string gatewayId, CancellationToken cancellationToken)
    {
        var current = options.Value;
        var interval = TimeSpan.FromMilliseconds(Math.Max(1000, current.SyncIntervalMs));
        var rawIntervals = await rawDataCaptureService.CaptureAsync(interval, current.RequireProductionContext, cancellationToken);
        var createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var processed = await localProcessingService.ProcessAsync(gatewayId, rawIntervals, createdAt, cancellationToken);
        var build = processed.RealtimeSnapshot;
        if (build.Payload.Items.Count == 0)
        {
            return build;
        }

        if (!current.RealtimeSnapshotsEnabled)
        {
            return build;
        }

        var dispatch = await outboxDispatcher.DispatchPendingAsync(gatewayId, [SyncOutboxTopics.RealtimeSnapshot], cancellationToken);
        if (dispatch.FailedCount > 0)
        {
            statusStore.MarkDropped($"{dispatch.FailedCount} realtime snapshot outbox items failed.");
        }
        else if (dispatch.SentCount > 0)
        {
            statusStore.MarkSuccess(createdAt, dispatch.SentCount);
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
