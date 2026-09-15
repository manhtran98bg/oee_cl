using System.Text.Json;
using System.Text.Json.Nodes;
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

        await CloseOpenMachineStateEventAsync(context, now, cancellationToken);
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
                var stopTime = DeltaOrZero(raw.StopTimeTotalSec, context.BaselineStopTimeTotalSec, raw.Machine, OeeSignalCodes.StopTimeTotal);
                var errorTime = DeltaOrZero(raw.ErrorTimeTotalSec, context.BaselineErrorTimeTotalSec, raw.Machine, OeeSignalCodes.ErrorTimeTotal);
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
                    "Realtime OEE snapshot calculated. Machine={Machine}, OrderId={OrderId}, SessionId={SessionId}, Product={Product}, State={State}, ActualQty={ActualQty}, PlannedQty={PlannedQty}, RunTime={RunTime}, StopTime={StopTime}, ErrorTime={ErrorTime}, ProductionTime={ProductionTime}, A={Availability}, P={Performance}, Q={Quality}, OEE={Oee}",
                    item.MachineCode,
                    item.OrderId,
                    item.SessionId,
                    item.ProductCode,
                    item.MachineState,
                    item.ActualQty,
                    item.PlannedQty,
                    runTime,
                    stopTime,
                    errorTime,
                    productionTime,
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
    IOeeLocalRepository repository,
    IRealtimeSnapshotBuilder snapshotBuilder,
    IMachineStateEventBuilder stateEventBuilder,
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
        var enqueued = 0;

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

        foreach (var item in snapshot.Payload.Items)
        {
            await EnqueueAsync(
                SyncOutboxTopics.RealtimeSnapshot,
                $"realtime_snapshot:{item.MachineCode}:{item.OrderId}:{item.SessionId}",
                MesSyncEndpointPaths.RealtimeSnapshots,
                item,
                createdAt,
                cancellationToken);
            enqueued++;
        }

        logger.LogDebug(
            "OEE local processing completed. RawIntervals={RawIntervals}, StateEvents={StateEvents}, SnapshotItems={SnapshotItems}, Enqueued={Enqueued}",
            rawIntervals.Count,
            stateEvents.Events.Count,
            snapshot.Payload.Items.Count,
            enqueued);

        return new OeeLocalProcessingResult(snapshot, stateEvents, enqueued);
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
                    ex,
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
        var items = new JsonArray();
        foreach (var message in messages)
        {
            var node = JsonNode.Parse(message.PayloadJson);
            if (node is not null)
            {
                items.Add(node);
            }
        }

        var payload = new JsonObject
        {
            ["schema_version"] = 1,
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
    IRealtimeSnapshotSyncStatusStore statusStore,
    ILogger<RealtimeSnapshotSyncService> logger) : IRealtimeSnapshotSyncService
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
            logger.LogDebug(
                "No realtime snapshot items were built. RawIntervals={RawIntervals}, Skipped={Skipped}",
                build.RawIntervalCount,
                build.SkippedItemCount);
            return build;
        }

        var payloadJson = JsonSerializer.Serialize(build.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        logger.LogInformation("Realtime OEE snapshot payload enqueued. PayloadJson={PayloadJson}", payloadJson);

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
