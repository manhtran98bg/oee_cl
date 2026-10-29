using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Application.Scheduling;
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
    ILogger<ProductionCommandService> logger,
    OeeProcessingGate? processingGate = null) : IProductionCommandService
{
    private readonly OeeProcessingGate _gate = processingGate ?? OeeProcessingGate.Shared;
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
            QuantityUpdateSource = request.QuantityUpdateSource,
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
        var waitTimer = Stopwatch.StartNew();
        using var lease = await _gate.EnterAsync(cancellationToken);
        var waitMs = waitTimer.ElapsedMilliseconds;
        var executionTimer = Stopwatch.StartNew();
        var response = await repository.ExecuteTransactionAsync(
            token => HandleItemCoreAsync(request, gatewayId, now, token), cancellationToken);
        var executionMs = executionTimer.ElapsedMilliseconds;
        if (waitMs >= 500)
        {
            logger.LogWarning(
                "Production command waited for OEE processing. Machine={Machine}, CommandCode={CommandCode}, GateWaitMs={GateWaitMs}, ExecutionMs={ExecutionMs}",
                request.MachineCode,
                request.CommandCode,
                waitMs,
                executionMs);
        }
        else
        {
            logger.LogDebug(
                "Production command timing. Machine={Machine}, CommandCode={CommandCode}, GateWaitMs={GateWaitMs}, ExecutionMs={ExecutionMs}",
                request.MachineCode,
                request.CommandCode,
                waitMs,
                executionMs);
        }

        return response;
    }

    private async Task<ProductionCommandResponse> HandleItemCoreAsync(
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
        if (!QuantityUpdateSources.TryNormalize(request.QuantityUpdateSource, out var quantityUpdateSource))
        {
            return Reject(machine, request.CommandCode, orderId, null, "quantity_update_source is required and must be oi or oee when action is start.");
        }

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
        var existingProducts = ReadProducts(context.ProductsJson);
        if (!isNewSession && existingProducts.Any(product =>
                request.Products!.Any(input => input.ProductCode.Equals(product.ProductId, StringComparison.OrdinalIgnoreCase)) &&
                !NormalizeSource(product.QuantityUpdateSource).Equals(quantityUpdateSource, StringComparison.OrdinalIgnoreCase)))
        {
            return Reject(machine, request.CommandCode, orderId, context.SessionId, "quantity_update_source differs from the active session; use production-quantity-sources to change it.");
        }

        context.ProductsJson = JsonSerializer.Serialize(
            ToOeeProducts(request.Products!, quantityUpdateSource, existingProducts),
            JsonOptions);
        context.ExtraJson = "{}";
        if (isNewSession)
        {
            ClearBaseline(context);
        }

        context.UpdatedAt = now;
        await repository.SaveProductionContextAsync(context, cancellationToken);
        await repository.EnsureProductionPeriodAsync(context, context.ActivePeriodStartAt, cancellationToken);
        repository.AfterCommit(() => productionContextCache.Upsert(context));
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
        repository.AfterCommit(() => productionContextCache.Remove(context.SessionId));
        return new ProductionCommandResponse(true, context.Machine, request.CommandCode.Trim(), responseStatus, context.OrderId, context.SessionId, "Accepted");
    }

    private async Task TryFinalizeSessionMetricAsync(
        string gatewayId,
        ProductionContext context,
        long endedAt,
        CancellationToken cancellationToken)
    {
        if (!ReadPrimaryProduct(context.ProductsJson).IsOeeQuantitySource)
        {
            logger.LogInformation(
                "Final session metric skipped because quantity source is OI. Machine={Machine}, OrderId={OrderId}, SessionId={SessionId}",
                context.Machine,
                context.OrderId,
                context.SessionId);
            return;
        }

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

        repository.AfterCommit(() =>
        {
            var added = orderQuantityCache.AddCompletedSession(context.Machine, context.OrderId, context.SessionId, metric.ActualQty);
            logger.LogInformation(
                "Final session metric saved. Machine={Machine}, OrderId={OrderId}, SessionId={SessionId}, ActualQty={ActualQty}, TotalQty={TotalQty}, AddedToOrderCache={AddedToOrderCache}",
                context.Machine,
                context.OrderId,
                context.SessionId,
                metric.ActualQty,
                metric.TotalQty,
                added);
        });
    }

    public async Task<ProductionRotationResult> RotateActiveSessionsAsync(
        string gatewayId, string machineCode, long executedAt, CancellationToken cancellationToken)
    {
        var contexts = (await repository.ListCapturableProductionContextsAsync(machineCode, cancellationToken))
            .Where(context => context.Status.Equals("active", StringComparison.OrdinalIgnoreCase) &&
                !context.OrderId.Equals(OeeTestProductionContext.OrderId, StringComparison.OrdinalIgnoreCase)).ToList();
        var boundary = await repository.GetLatestRawIntervalAsync(machineCode, cancellationToken);
        var transitions = new List<SessionTransition>();
        var warnings = new List<string>();
        foreach (var previous in contexts)
        {
            if (boundary is null || previous.BaselineCapturedAt <= 0 ||
                boundary.ShotOkTotal < previous.BaselineShotOkTotal || boundary.ShotNgTotal < previous.BaselineShotNgTotal ||
                boundary.RunTimeTotalSec < previous.BaselineRunTimeTotalSec || boundary.StopTimeTotalSec < previous.BaselineStopTimeTotalSec ||
                boundary.ErrorTimeTotalSec < previous.BaselineErrorTimeTotalSec)
                warnings.Add($"Lệnh {previous.OrderId}: không đủ counter/baseline hợp lệ để chốt metric cũ.");

            await EndSessionAsync(new ProductionCommandItemRequest { CommandCode = "SCHEDULE", OrderId = previous.OrderId },
                gatewayId, previous.Machine, previous.OrderId, executedAt, "paused", "pause", cancellationToken);
            var next = new ProductionContext
            {
                SessionId = $"SCH-{Guid.NewGuid():N}", Machine = previous.Machine, Status = "active",
                OrderId = previous.OrderId, ServerOrderId = previous.ServerOrderId,
                ProductsJson = previous.ProductsJson, ExtraJson = previous.ExtraJson,
                ActivePeriodStartAt = executedAt, UpdatedAt = executedAt,
                CurrentPlcPeriodIndex = await repository.GetNextPlcPeriodIndexAsync(previous.Machine, previous.OrderId, cancellationToken)
            };
            if (boundary is not null)
            {
                next.BaselineRawId = boundary.Id;
                next.BaselineCapturedAt = boundary.ReadAt;
                next.BaselineShotOkTotal = boundary.ShotOkTotal;
                next.BaselineShotNgTotal = boundary.ShotNgTotal;
                next.BaselineRunTimeTotalSec = boundary.RunTimeTotalSec;
                next.BaselineStopTimeTotalSec = boundary.StopTimeTotalSec;
                next.BaselineErrorTimeTotalSec = boundary.ErrorTimeTotalSec;
                next.BaselineCycleTimeMs = boundary.CycleTimeMs;
            }
            await repository.SaveProductionContextAsync(next, cancellationToken);
            await repository.EnsureProductionPeriodAsync(next, executedAt, cancellationToken);
            repository.AfterCommit(() => productionContextCache.Upsert(next));
            transitions.Add(new(previous.OrderId, previous.SessionId, next.SessionId));
        }
        return new(transitions, warnings.Count == 0 ? null : string.Join(" ", warnings));
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

    private static IReadOnlyList<OeeProductDefinition> ToOeeProducts(
        IReadOnlyList<ProductionCommandProduct> products,
        string quantityUpdateSource,
        IReadOnlyList<OeeProductDefinition> existingProducts)
    {
        var existingByCode = existingProducts.ToDictionary(product => product.ProductId, StringComparer.OrdinalIgnoreCase);
        return products.Select(product =>
        {
            existingByCode.TryGetValue(product.ProductCode.Trim(), out var existing);
            return new OeeProductDefinition
            {
                ProductId = product.ProductCode.Trim(),
                MoldCode = Normalize(product.MoldCode),
                Gain = product.Cavity,
                CycleTime = product.CycleTime,
                Target = product.TargetQty,
                QuantityUpdateSource = quantityUpdateSource,
                OeeAccumulatedShotOk = existing?.OeeAccumulatedShotOk ?? 0,
                OeeAccumulatedShotNg = existing?.OeeAccumulatedShotNg ?? 0,
                OeeAccumulatedRunTimeSec = existing?.OeeAccumulatedRunTimeSec ?? 0,
                OeeAccumulatedStopTimeSec = existing?.OeeAccumulatedStopTimeSec ?? 0,
                OeeAccumulatedErrorTimeSec = existing?.OeeAccumulatedErrorTimeSec ?? 0,
                OeeAccumulatedProductionTimeSec = existing?.OeeAccumulatedProductionTimeSec ?? 0,
                OeeBaselineShotOkTotal = existing?.OeeBaselineShotOkTotal,
                OeeBaselineShotNgTotal = existing?.OeeBaselineShotNgTotal,
                OeeBaselineCapturedAt = existing?.OeeBaselineCapturedAt,
                OeeBaselineRunTimeTotalSec = existing?.OeeBaselineRunTimeTotalSec,
                OeeBaselineStopTimeTotalSec = existing?.OeeBaselineStopTimeTotalSec,
                OeeBaselineErrorTimeTotalSec = existing?.OeeBaselineErrorTimeTotalSec,
                OeeSegmentStartedAt = existing?.OeeSegmentStartedAt,
                OeeResumedAt = existing?.OeeResumedAt
            };
        }).ToList();
    }

    private static IReadOnlyList<OeeProductDefinition> ReadProducts(string productsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<List<OeeProductDefinition>>(productsJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static OeeProductDefinition ReadPrimaryProduct(string productsJson) =>
        ReadProducts(productsJson).FirstOrDefault(product => !string.IsNullOrWhiteSpace(product.ProductId)) ??
        new OeeProductDefinition { ProductId = "UNKNOWN_PRODUCT" };

    private static string NormalizeSource(string? source) =>
        QuantityUpdateSources.TryNormalize(source, out var normalized) ? normalized : QuantityUpdateSources.Oee;

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

internal static class OeeQuantityCalculator
{
    public static long FromShotCount(long shotCount, decimal cavity)
    {
        var effectiveCavity = cavity > 0 ? cavity : 1m;
        return decimal.ToInt64(decimal.Floor(shotCount * effectiveCavity));
    }
}

public sealed class ProductionQuantitySourceService(
    IOeeLocalRepository repository,
    IConfigRepository configRepository,
    IProductionContextCache productionContextCache,
    ILogger<ProductionQuantitySourceService> logger,
    OeeProcessingGate processingGate) : IProductionQuantitySourceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<ProductionQuantitySourceResponse> HandleAsync(
        ProductionQuantitySourceBatchRequest request,
        CancellationToken cancellationToken)
    {
        var item = request.Items![0];
        var eventCode = item.EventCode?.Trim() ?? string.Empty;
        var machine = item.MachineCode?.Trim() ?? string.Empty;
        var orderId = item.OrderId?.Trim() ?? string.Empty;
        var productCode = item.ProductCode?.Trim() ?? string.Empty;
        var source = item.QuantityUpdateSource?.Trim().ToLowerInvariant() ?? string.Empty;
        var processedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var requestHash = BuildRequestHash(machine, orderId, productCode, source);

        using var lease = await processingGate.EnterAsync(cancellationToken);
        return await repository.ExecuteTransactionAsync(async token =>
        {
            var existingEvent = await repository.GetProductionQuantitySourceEventAsync(eventCode, token);
            if (existingEvent is not null)
            {
                return existingEvent.RequestHash.Equals(requestHash, StringComparison.OrdinalIgnoreCase)
                    ? ToResponse(existingEvent)
                    : Reject(eventCode, "event_code_conflict", "event_code was already used for a different payload.");
            }

            ProductionQuantitySourceResponse response;
            ProductionContext? context = null;
            string? previousSource = null;

            if (!QuantityUpdateSources.TryNormalize(source, out source))
            {
                response = Reject(eventCode, "invalid_quantity_update_source", "quantity_update_source must be oi or oee.");
            }
            else if (await configRepository.GetMachineByCodeAsync(machine, includeDetails: false, token) is null)
            {
                response = Reject(eventCode, "machine_not_found", $"machine_code {machine} does not exist.");
            }
            else if ((context = await repository.GetActiveProductionContextAsync(machine, orderId, token)) is null)
            {
                response = Reject(eventCode, "order_not_found", $"order_id {orderId} is not active on machine {machine}.");
            }
            else
            {
                var products = ReadProducts(context.ProductsJson);
                var product = products.FirstOrDefault(candidate =>
                    candidate.ProductId.Equals(productCode, StringComparison.OrdinalIgnoreCase));
                if (product is null)
                {
                    response = Reject(eventCode, "product_not_found", $"product_code {productCode} does not exist in the active order.");
                }
                else
                {
                    previousSource = NormalizeSource(product.QuantityUpdateSource);
                    if (!previousSource.Equals(source, StringComparison.OrdinalIgnoreCase))
                    {
                        var latestRaw = await repository.GetLatestRawIntervalAsync(machine, token);
                        if (previousSource == QuantityUpdateSources.Oee && source == QuantityUpdateSources.Oi)
                        {
                            AccumulateCurrentOeeSegment(product, context, latestRaw, processedAt);
                        }
                        else if (previousSource == QuantityUpdateSources.Oi && source == QuantityUpdateSources.Oee)
                        {
                            SeedOeeBaseline(product, latestRaw, processedAt);
                        }

                        product.QuantityUpdateSource = source;
                        context.ProductsJson = JsonSerializer.Serialize(products, JsonOptions);
                        context.UpdatedAt = processedAt;
                        await repository.SaveProductionContextAsync(context, token);
                        await repository.RemovePendingQuantityOutboxMessagesAsync(machine, orderId, context.SessionId, token);
                        if (source == QuantityUpdateSources.Oi)
                        {
                            await repository.RemoveOpenProductionMetricsAsync(machine, orderId, context.SessionId, token);
                        }

                        var cachedContext = context;
                        repository.AfterCommit(() => productionContextCache.Upsert(cachedContext));
                    }

                    response = new ProductionQuantitySourceResponse(eventCode, true);
                }
            }

            await repository.SaveProductionQuantitySourceEventAsync(new ProductionQuantitySourceEvent
            {
                EventCode = eventCode,
                RequestHash = requestHash,
                GatewayId = request.GatewayId?.Trim() ?? string.Empty,
                RequestCreatedAt = request.CreatedAt,
                Machine = machine,
                OrderId = orderId,
                SessionId = context?.SessionId,
                ProductCode = productCode,
                PreviousSource = previousSource,
                QuantityUpdateSource = source,
                Accepted = response.Accepted,
                ErrorCode = response.Error?.Code,
                ErrorMessage = response.Error?.Message,
                ProcessedAt = processedAt
            }, token);

            logger.LogInformation(
                "Production quantity source event handled. EventCode={EventCode}, Machine={Machine}, OrderId={OrderId}, ProductCode={ProductCode}, Source={Source}, Accepted={Accepted}, ErrorCode={ErrorCode}",
                eventCode,
                machine,
                orderId,
                productCode,
                source,
                response.Accepted,
                response.Error?.Code);
            return response;
        }, cancellationToken);
    }

    private static void AccumulateCurrentOeeSegment(
        OeeProductDefinition product,
        ProductionContext context,
        PlcRawInterval? raw,
        long endedAt)
    {
        if (raw is null)
        {
            return;
        }

        var baselineOk = product.OeeBaselineShotOkTotal ??
            (context.BaselineCapturedAt > 0 ? context.BaselineShotOkTotal : raw.ShotOkTotal);
        var baselineNg = product.OeeBaselineShotNgTotal ??
            (context.BaselineCapturedAt > 0 ? context.BaselineShotNgTotal : raw.ShotNgTotal);
        if (raw.ShotOkTotal >= baselineOk)
        {
            product.OeeAccumulatedShotOk += raw.ShotOkTotal - baselineOk;
        }

        if (raw.ShotNgTotal >= baselineNg)
        {
            product.OeeAccumulatedShotNg += raw.ShotNgTotal - baselineNg;
        }

        product.OeeAccumulatedRunTimeSec += DeltaOrZero(
            raw.RunTimeTotalSec,
            product.OeeBaselineRunTimeTotalSec ?? context.BaselineRunTimeTotalSec);
        product.OeeAccumulatedStopTimeSec += DeltaOrZero(
            raw.StopTimeTotalSec,
            product.OeeBaselineStopTimeTotalSec ?? context.BaselineStopTimeTotalSec);
        product.OeeAccumulatedErrorTimeSec += DeltaOrZero(
            raw.ErrorTimeTotalSec,
            product.OeeBaselineErrorTimeTotalSec ?? context.BaselineErrorTimeTotalSec);
        product.OeeAccumulatedProductionTimeSec += Math.Max(
            0,
            endedAt - (product.OeeSegmentStartedAt ?? context.ActivePeriodStartAt));

        product.OeeBaselineShotOkTotal = raw.ShotOkTotal;
        product.OeeBaselineShotNgTotal = raw.ShotNgTotal;
        product.OeeBaselineCapturedAt = raw.ReadAt;
        product.OeeBaselineRunTimeTotalSec = raw.RunTimeTotalSec;
        product.OeeBaselineStopTimeTotalSec = raw.StopTimeTotalSec;
        product.OeeBaselineErrorTimeTotalSec = raw.ErrorTimeTotalSec;
        product.OeeSegmentStartedAt = null;
    }

    private static void SeedOeeBaseline(OeeProductDefinition product, PlcRawInterval? raw, long resumedAt)
    {
        product.OeeBaselineShotOkTotal = raw?.ShotOkTotal;
        product.OeeBaselineShotNgTotal = raw?.ShotNgTotal;
        product.OeeBaselineCapturedAt = raw?.ReadAt;
        product.OeeBaselineRunTimeTotalSec = raw?.RunTimeTotalSec;
        product.OeeBaselineStopTimeTotalSec = raw?.StopTimeTotalSec;
        product.OeeBaselineErrorTimeTotalSec = raw?.ErrorTimeTotalSec;
        product.OeeSegmentStartedAt = resumedAt;
        product.OeeResumedAt = resumedAt;
    }

    private static long DeltaOrZero(long current, long baseline) => Math.Max(0, current - baseline);

    private static IReadOnlyList<OeeProductDefinition> ReadProducts(string productsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<List<OeeProductDefinition>>(productsJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string NormalizeSource(string? source) =>
        QuantityUpdateSources.TryNormalize(source, out var normalized) ? normalized : QuantityUpdateSources.Oee;

    private static string BuildRequestHash(string machine, string orderId, string productCode, string source)
    {
        var canonical = $"{machine.Trim().ToUpperInvariant()}|{orderId.Trim().ToUpperInvariant()}|{productCode.Trim().ToUpperInvariant()}|{source.Trim().ToLowerInvariant()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static ProductionQuantitySourceResponse ToResponse(ProductionQuantitySourceEvent sourceEvent) =>
        new(
            sourceEvent.EventCode,
            sourceEvent.Accepted,
            sourceEvent.Accepted
                ? null
                : new ProductionQuantitySourceError(
                    sourceEvent.ErrorCode ?? "rejected",
                    sourceEvent.ErrorMessage ?? "The event was rejected."));

    private static ProductionQuantitySourceResponse Reject(string eventCode, string code, string message) =>
        new(eventCode, false, new ProductionQuantitySourceError(code, message));
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
        var latestRawByMachine = await repository.GetLatestRawIntervalsAsync(
            enabledMachines.Select(machine => machine.MachineCode).ToArray(),
            cancellationToken);

        foreach (var machine in enabledMachines)
        {
            var hasCurrentRaw = currentRawByMachine.TryGetValue(machine.MachineCode, out var currentRaw);
            var contexts = productionContextCache.GetCapturableByMachine(machine.MachineCode);

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
                latestRawByMachine.TryGetValue(machine.MachineCode, out effectiveRaw);
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
                    SeedProductBaselines(context, effectiveRaw);
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
        if (!product.IsOeeQuantitySource)
        {
            return CreateOiContextItem(context, product, state);
        }

        var baselineShotOk = product.OeeBaselineShotOkTotal ?? context.BaselineShotOkTotal;
        var baselineShotNg = product.OeeBaselineShotNgTotal ?? context.BaselineShotNgTotal;
        var goodShots = product.OeeAccumulatedShotOk + DeltaOrZero(raw.ShotOkTotal, baselineShotOk);
        var ngShots = product.OeeAccumulatedShotNg + DeltaOrZero(raw.ShotNgTotal, baselineShotNg);
        var runTime = product.OeeAccumulatedRunTimeSec + DeltaOrZero(
            raw.RunTimeTotalSec,
            product.OeeBaselineRunTimeTotalSec ?? context.BaselineRunTimeTotalSec);
        var productionTime = product.OeeAccumulatedProductionTimeSec + Math.Max(
            0,
            metricAt - (product.OeeSegmentStartedAt ?? context.ActivePeriodStartAt));
        var shotQty = checked(goodShots + ngShots);
        var actualQty = OeeQuantityCalculator.FromShotCount(shotQty, product.Gain);
        var cycleTimeSeconds = product.EffectiveCycleTime > 0
            ? product.EffectiveCycleTime
            : raw.CycleTimeMs > 0 ? raw.CycleTimeMs / 1000m : 0m;
        var plannedQty = cycleTimeSeconds > 0
            ? Decimal.Floor(productionTime / cycleTimeSeconds) * product.Gain
            : 0m;
        var availability = Percent(runTime, productionTime);
        var performance = plannedQty > 0 ? Percent(actualQty, plannedQty) : 0m;
        var quality = shotQty > 0 ? Percent(goodShots, shotQty) : 0m;
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
        if (!product.IsOeeQuantitySource)
        {
            return CreateOiContextItem(context, product, state);
        }

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

    private static RealtimeSnapshotItemPayload CreateOiContextItem(
        ProductionContext context,
        OeeProductDefinition product,
        string state) =>
        new(
            context.Machine,
            context.OrderId,
            context.SessionId,
            product.ProductId,
            product.MoldCode,
            state,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            EmptyExtra());

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

    private static void SeedProductBaselines(ProductionContext context, PlcRawInterval raw)
    {
        try
        {
            var products = JsonSerializer.Deserialize<List<OeeProductDefinition>>(context.ProductsJson, JsonOptions) ?? [];
            foreach (var product in products.Where(product => product.IsOeeQuantitySource))
            {
                product.OeeBaselineShotOkTotal ??= raw.ShotOkTotal;
                product.OeeBaselineShotNgTotal ??= raw.ShotNgTotal;
                product.OeeBaselineCapturedAt ??= raw.ReadAt;
                product.OeeBaselineRunTimeTotalSec ??= raw.RunTimeTotalSec;
                product.OeeBaselineStopTimeTotalSec ??= raw.StopTimeTotalSec;
                product.OeeBaselineErrorTimeTotalSec ??= raw.ErrorTimeTotalSec;
                product.OeeSegmentStartedAt ??= context.ActivePeriodStartAt;
            }

            context.ProductsJson = JsonSerializer.Serialize(products, JsonOptions);
        }
        catch (JsonException)
        {
            // Keep legacy product JSON unchanged; context baseline remains the fallback.
        }
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
        var contextsByMachine = rawIntervals
            .Select(raw => raw.Machine)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                machine => machine,
                machine => productionContextCache.GetCapturableByMachine(machine),
                StringComparer.OrdinalIgnoreCase);
        var openEvents = new Dictionary<string, MachineStateEvent>(
            await repository.GetOpenMachineStateEventsAsync(
                contextsByMachine.Values.SelectMany(contexts => contexts).Select(context => context.SessionId).ToArray(),
                cancellationToken),
            StringComparer.OrdinalIgnoreCase);

        foreach (var raw in rawIntervals.OrderBy(item => item.ReadAt).ThenBy(item => item.Machine, StringComparer.OrdinalIgnoreCase))
        {
            var contexts = contextsByMachine.GetValueOrDefault(raw.Machine) ?? [];

            if (contexts.Count == 0)
            {
                skipped++;
                continue;
            }

            foreach (var context in contexts)
            {
                openEvents.TryGetValue(context.SessionId, out var openEvent);

                if (openEvent is null)
                {
                    var created = CreateEvent(gatewayId, context, raw.RunState, raw.ReadAt, raw.ReadAt, isOpen: true, createdAt);
                    changed.Add(created);
                    openEvents[context.SessionId] = created;
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
                    var created = CreateEvent(gatewayId, context, raw.RunState, raw.ReadAt, raw.ReadAt, isOpen: true, createdAt);
                    changed.Add(created);
                    openEvents[context.SessionId] = created;

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
                var next = CreateEvent(gatewayId, context, raw.RunState, raw.ReadAt, raw.ReadAt, isOpen: true, createdAt);
                changed.Add(next);
                openEvents[context.SessionId] = next;
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
        var realtime = await ProcessRealtimeAsync(gatewayId, rawIntervals, createdAt, cancellationToken);
        var metrics = await ProcessProductionMetricsAsync(gatewayId, rawIntervals, createdAt, cancellationToken);
        return realtime with
        {
            ProductionMetrics = metrics,
            EnqueuedOutboxCount = realtime.EnqueuedOutboxCount +
                (options.Value.ProductionMetricsEnabled ? metrics.Metrics.Count : 0)
        };
    }

    public async Task<OeeLocalProcessingResult> ProcessRealtimeAsync(
        string gatewayId,
        IReadOnlyCollection<PlcRawInterval> rawIntervals,
        long createdAt,
        CancellationToken cancellationToken)
    {
        var stateEvents = await stateEventBuilder.BuildAsync(gatewayId, rawIntervals, createdAt, cancellationToken);
        var snapshot = await snapshotBuilder.BuildAsync(gatewayId, rawIntervals, createdAt, cancellationToken);
        var current = options.Value;
        var messages = new List<SyncOutboxMessage>();

        if (current.MachineStateEventsEnabled)
        {
            foreach (var stateEvent in stateEvents.Events)
            {
                messages.Add(CreateOutboxMessage(
                    SyncOutboxTopics.MachineStateEvent,
                    $"machine_state_event:{stateEvent.EventId}",
                    MesSyncEndpointPaths.MachineStateEvents,
                    ToPayloadItem(stateEvent),
                    createdAt));
            }
        }

        if (current.RealtimeSnapshotsEnabled)
        {
            var retainedDedupeKeys = new List<string>(snapshot.Payload.Items.Count);
            foreach (var item in snapshot.Payload.Items)
            {
                var dedupeKey = BuildRealtimeSnapshotDedupeKey(item);
                messages.Add(CreateOutboxMessage(
                    SyncOutboxTopics.RealtimeSnapshot,
                    dedupeKey,
                    MesSyncEndpointPaths.RealtimeSnapshots,
                    item,
                    createdAt));
                retainedDedupeKeys.Add(dedupeKey);
            }

            await repository.UpsertSyncOutboxMessagesAsync(messages, cancellationToken);
            messages.Clear();
            await repository.RemoveStaleRealtimeSnapshotMessagesAsync(retainedDedupeKeys, cancellationToken);
        }

        if (messages.Count > 0)
        {
            await repository.UpsertSyncOutboxMessagesAsync(messages, cancellationToken);
        }

        var enqueued = (current.MachineStateEventsEnabled ? stateEvents.Events.Count : 0) +
            (current.RealtimeSnapshotsEnabled ? snapshot.Payload.Items.Count : 0);
        logger.LogDebug(
            "OEE realtime processing completed. RawIntervals={RawIntervals}, StateEvents={StateEvents}, SnapshotItems={SnapshotItems}, Enqueued={Enqueued}",
            rawIntervals.Count,
            stateEvents.Events.Count,
            snapshot.Payload.Items.Count,
            enqueued);

        return new OeeLocalProcessingResult(snapshot, stateEvents, new ProductionMetricBuildResult([], 0), enqueued);
    }

    public async Task<ProductionMetricBuildResult> ProcessProductionMetricsAsync(
        string gatewayId,
        IReadOnlyCollection<PlcRawInterval> rawIntervals,
        long createdAt,
        CancellationToken cancellationToken)
    {
        var result = await productionMetricBuilder.BuildAsync(gatewayId, rawIntervals, createdAt, cancellationToken);
        if (!options.Value.ProductionMetricsEnabled || result.Metrics.Count == 0)
        {
            return result;
        }

        var messages = result.Metrics.Select(metric => CreateOutboxMessage(
            SyncOutboxTopics.ProductionMetric,
            $"production_metric:{metric.MetricId}",
            MesSyncEndpointPaths.ProductionMetrics,
            ToPayloadItem(metric),
            createdAt)).ToArray();
        await repository.UpsertSyncOutboxMessagesAsync(messages, cancellationToken);
        logger.LogDebug(
            "OEE production metric processing completed. RawIntervals={RawIntervals}, Metrics={Metrics}",
            rawIntervals.Count,
            result.Metrics.Count);
        return result;
    }

    private static SyncOutboxMessage CreateOutboxMessage<TPayload>(
        string topic,
        string dedupeKey,
        string endpointPath,
        TPayload payload,
        long createdAt)
    {
        var payloadJson = JsonSerializer.Serialize(payload, PayloadJsonOptions);
        return new SyncOutboxMessage
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
        };
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

            foreach (var context in contexts)
            {
                if (context.BaselineCapturedAt <= 0)
                {
                    skipped++;
                    continue;
                }

                var product = ReadPrimaryProduct(context.ProductsJson);
                if (!product.IsOeeQuantitySource)
                {
                    skipped++;
                    continue;
                }

                var sessionProductionTime = product.OeeAccumulatedProductionTimeSec + Math.Max(
                    0,
                    createdAt - (product.OeeSegmentStartedAt ?? context.ActivePeriodStartAt));
                var sessionMetric = TryBuildMetric(
                    gatewayId,
                    "session",
                    context.ActivePeriodStartAt,
                    createdAt,
                    isFinal: false,
                    context,
                    product,
                    raw,
                    product.OeeBaselineShotOkTotal ?? context.BaselineShotOkTotal,
                    product.OeeBaselineShotNgTotal ?? context.BaselineShotNgTotal,
                    product.OeeBaselineRunTimeTotalSec ?? context.BaselineRunTimeTotalSec,
                    product.OeeBaselineStopTimeTotalSec ?? context.BaselineStopTimeTotalSec,
                    product.OeeBaselineErrorTimeTotalSec ?? context.BaselineErrorTimeTotalSec,
                    createdAt,
                    productionTimeOverride: sessionProductionTime,
                    accumulatedShotOk: product.OeeAccumulatedShotOk,
                    accumulatedShotNg: product.OeeAccumulatedShotNg,
                    accumulatedRunTime: product.OeeAccumulatedRunTimeSec,
                    accumulatedStopTime: product.OeeAccumulatedStopTimeSec,
                    accumulatedErrorTime: product.OeeAccumulatedErrorTimeSec);
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
        if (!product.IsOeeQuantitySource)
        {
            logger.LogInformation(
                "Final production metric skipped because quantity source is OI. Machine={Machine}, OrderId={OrderId}, SessionId={SessionId}",
                context.Machine,
                context.OrderId,
                context.SessionId);
            return null;
        }

        var sessionProductionTime = product.OeeAccumulatedProductionTimeSec + Math.Max(
            0,
            stoppedAt - (product.OeeSegmentStartedAt ?? context.ActivePeriodStartAt));
        var metric = TryBuildMetric(
            gatewayId,
            "session",
            context.ActivePeriodStartAt,
            stoppedAt,
            isFinal: true,
            context,
            product,
            raw,
            product.OeeBaselineShotOkTotal ?? context.BaselineShotOkTotal,
            product.OeeBaselineShotNgTotal ?? context.BaselineShotNgTotal,
            product.OeeBaselineRunTimeTotalSec ?? context.BaselineRunTimeTotalSec,
            product.OeeBaselineStopTimeTotalSec ?? context.BaselineStopTimeTotalSec,
            product.OeeBaselineErrorTimeTotalSec ?? context.BaselineErrorTimeTotalSec,
            stoppedAt,
            productionTimeOverride: sessionProductionTime,
            accumulatedShotOk: product.OeeAccumulatedShotOk,
            accumulatedShotNg: product.OeeAccumulatedShotNg,
            accumulatedRunTime: product.OeeAccumulatedRunTimeSec,
            accumulatedStopTime: product.OeeAccumulatedStopTimeSec,
            accumulatedErrorTime: product.OeeAccumulatedErrorTimeSec);

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

        if (product.OeeResumedAt is long resumedAt && effectiveStart < resumedAt)
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
        long? productionTimeOverride = null,
        long accumulatedShotOk = 0,
        long accumulatedShotNg = 0,
        long accumulatedRunTime = 0,
        long accumulatedStopTime = 0,
        long accumulatedErrorTime = 0)
    {
        var goodQty = accumulatedShotOk + currentRaw.ShotOkTotal - baselineShotOk;
        var ngQty = accumulatedShotNg + currentRaw.ShotNgTotal - baselineShotNg;
        var runTime = accumulatedRunTime + currentRaw.RunTimeTotalSec - baselineRunTime;
        var stopTime = accumulatedStopTime + currentRaw.StopTimeTotalSec - baselineStopTime;
        var errorTime = accumulatedErrorTime + currentRaw.ErrorTimeTotalSec - baselineErrorTime;
        if (goodQty < 0 || ngQty < 0 || runTime < 0 || stopTime < 0 || errorTime < 0)
        {
            return null;
        }

        var shotQty = checked(goodQty + ngQty);
        var actualQty = OeeQuantityCalculator.FromShotCount(shotQty, product.Gain);
        var productionTime = productionTimeOverride ?? Math.Max(0, bucketEnd - bucketStart);
        var cycleTime = product.EffectiveCycleTime > 0
            ? product.EffectiveCycleTime
            : currentRaw.CycleTimeMs > 0 ? currentRaw.CycleTimeMs / 1000m : 0m;
        var plannedQty = cycleTime > 0
            ? decimal.Floor(productionTime / cycleTime) * product.Gain
            : 0m;
        var availability = Percent(runTime, productionTime);
        var performance = plannedQty > 0 ? Percent(actualQty, plannedQty) : 0m;
        var quality = shotQty > 0 ? Percent(goodQty, shotQty) : 0m;
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
            var dispatchedMessages = group.ToArray();
            try
            {
                var payload = BuildBatchPayload(gatewayId, now, dispatchedMessages);
                // logger.LogInformation("Payload {Payload}", payload);
                logger.LogInformation(
                    "MES sync outbox batch sent. Endpoint={Endpoint}, Items={ItemCount}, payload = {}",
                    group.Key,
                    dispatchedMessages.Length,
                        payload);
                await httpClient.SendAsync(group.Key, payload, cancellationToken);
                await repository.MarkSyncOutboxMessagesSucceededAsync(dispatchedMessages, now, cancellationToken);
                sent += dispatchedMessages.Length;
                logger.LogInformation(
                    "MES sync outbox batch sent. Endpoint={Endpoint}, Items={ItemCount}",
                    group.Key,
                    dispatchedMessages.Length);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var nextAttemptAt = now + CalculateRetryDelaySeconds(group.Max(message => message.AttemptCount), current.RetryCount);
                await repository.MarkSyncOutboxMessagesFailedAsync(dispatchedMessages, ex.Message, nextAttemptAt, cancellationToken);
                failed += dispatchedMessages.Length;
                logger.LogWarning(
                    null,
                    "MES sync outbox batch failed. Endpoint={Endpoint}, Items={ItemCount}, NextAttemptAt={NextAttemptAt}",
                    group.Key,
                    dispatchedMessages.Length,
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
    ProductionMetricBuildSchedule metricBuildSchedule,
    ILogger<RealtimeSnapshotSyncService> logger,
    OeeProcessingGate? processingGate = null) : IRealtimeSnapshotSyncService
{
    public async Task<RealtimeSnapshotBuildResult> SyncAsync(string gatewayId, CancellationToken cancellationToken)
    {
        var current = options.Value;
        var interval = TimeSpan.FromMilliseconds(Math.Max(1000, current.SyncIntervalMs));
        RealtimeSnapshotBuildResult build;
        IReadOnlyCollection<PlcRawInterval> rawIntervals;
        long createdAt;
        var total = Stopwatch.StartNew();
        var wait = Stopwatch.StartNew();
        using (await (processingGate ?? OeeProcessingGate.Shared).EnterAsync(cancellationToken))
        {
            var waitedMs = wait.ElapsedMilliseconds;
            var critical = Stopwatch.StartNew();
            rawIntervals = await rawDataCaptureService.CaptureAsync(interval, current.RequireProductionContext, cancellationToken);
            createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var processed = await localProcessingService.ProcessRealtimeAsync(gatewayId, rawIntervals, createdAt, cancellationToken);
            build = processed.RealtimeSnapshot;
            logger.LogDebug(
                "Realtime OEE critical section completed. GateWaitMs={GateWaitMs}, CriticalMs={CriticalMs}, RawIntervals={RawIntervals}, SnapshotItems={SnapshotItems}",
                waitedMs,
                critical.ElapsedMilliseconds,
                rawIntervals.Count,
                build.Payload.Items.Count);
        }

        var metricMs = 0L;
        if (current.ProductionMetricsEnabled &&
            rawIntervals.Count > 0 &&
            metricBuildSchedule.TryAcquire(createdAt, current.ProductionMetricHourUpdateIntervalMs))
        {
            var metricTimer = Stopwatch.StartNew();
            try
            {
                await localProcessingService.ProcessProductionMetricsAsync(
                    gatewayId,
                    rawIntervals,
                    createdAt,
                    cancellationToken);
            }
            catch
            {
                metricBuildSchedule.ReleaseForRetry();
                throw;
            }

            metricMs = metricTimer.ElapsedMilliseconds;
        }

        logger.LogDebug(
            "Realtime OEE cycle completed. TotalMs={TotalMs}, ProductionMetricMs={ProductionMetricMs}",
            total.ElapsedMilliseconds,
            metricMs);

        return build;
    }
}

public sealed class ProductionMetricBuildSchedule
{
    private readonly object _sync = new();
    private long _nextBuildAt;

    public bool TryAcquire(long now, int intervalMs)
    {
        lock (_sync)
        {
            if (now < _nextBuildAt)
            {
                return false;
            }

            _nextBuildAt = now + Math.Max(1, intervalMs / 1000);
            return true;
        }
    }

    public void ReleaseForRetry()
    {
        lock (_sync)
        {
            _nextBuildAt = 0;
        }
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
