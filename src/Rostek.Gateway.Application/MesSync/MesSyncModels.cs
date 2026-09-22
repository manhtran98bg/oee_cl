using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rostek.Gateway.Application.MesSync;

public sealed class MesSyncOptions
{
    public bool Enabled { get; set; }
    public bool RealtimeSnapshotsEnabled { get; set; } = true;
    public bool MachineStateEventsEnabled { get; set; } = true;
    public bool ProductionMetricsEnabled { get; set; } = true;
    public string BaseUrl { get; set; } = string.Empty;
    public string BearerToken { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
    public int BatchSize { get; set; } = 100;
    public int SyncIntervalMs { get; set; } = 5000;
    public int MachineStateEventSyncIntervalMs { get; set; } = 60000;
    public int ProductionMetricSyncIntervalMs { get; set; } = 60000;
    public int ProductionMetricHourUpdateIntervalMs { get; set; } = 60000;
    public int ProductionMetricDayUpdateIntervalMs { get; set; } = 300000;
    public string ProductionMetricTimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";
    public int MachineStateEventGapThresholdMs { get; set; } = 15000;
    public bool RequireProductionContext { get; set; }
}

public sealed class ProductionCommandRequest
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("command_code")]
    public string CommandCode { get; init; } = string.Empty;

    [JsonPropertyName("machine_code")]
    public string MachineCode { get; init; } = string.Empty;

    [JsonPropertyName("machine_name")]
    public string? MachineName { get; init; }

    [JsonPropertyName("action")]
    public string Action { get; init; } = string.Empty;

    [JsonPropertyName("occurred_at_unix_seconds")]
    public long? OccurredAtUnixTimeSeconds { get; init; }

    [JsonPropertyName("production_order_code")]
    public string? ProductionOrderCode { get; init; }

    [JsonPropertyName("products")]
    public IReadOnlyList<ProductionCommandProduct>? Products { get; init; }

    [JsonPropertyName("extra")]
    public JsonElement? Extra { get; init; }
}

public sealed class ProductionCommandBatchRequest
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("gateway_id")]
    public string GatewayId { get; init; } = string.Empty;

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; init; }

    [JsonPropertyName("items")]
    public IReadOnlyList<ProductionCommandItemRequest>? Items { get; init; }
}

public sealed class ProductionCommandItemRequest
{
    [JsonPropertyName("command_code")]
    public string CommandCode { get; init; } = string.Empty;

    [JsonPropertyName("machine_code")]
    public string MachineCode { get; init; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; init; } = string.Empty;

    [JsonPropertyName("order_id")]
    public string? OrderId { get; init; }

    [JsonPropertyName("products")]
    public IReadOnlyList<ProductionCommandProduct>? Products { get; init; }
}

public sealed class ProductionCommandProduct
{
    [JsonPropertyName("product_code")]
    public string ProductCode { get; init; } = string.Empty;

    [JsonPropertyName("mold_code")]
    public string? MoldCode { get; init; }

    [JsonPropertyName("cavity")]
    public decimal Cavity { get; init; }

    [JsonPropertyName("cycle_time")]
    public decimal CycleTime { get; init; }

    [JsonPropertyName("target_qty")]
    public int TargetQty { get; init; }
}

public sealed record ProductionCommandResponse(
    [property: JsonPropertyName("accepted")] bool Accepted,
    [property: JsonPropertyName("machine_code")] string MachineCode,
    [property: JsonPropertyName("command_code")] string CommandCode,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("order_id")] string? OrderId,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("message")] string? Message);

public sealed record ProductionCommandBatchResponse(
    [property: JsonPropertyName("accepted")] bool Accepted,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("gateway_id")] string GatewayId,
    [property: JsonPropertyName("created_at")] long CreatedAt,
    [property: JsonPropertyName("accepted_count")] int AcceptedCount,
    [property: JsonPropertyName("rejected_count")] int RejectedCount,
    [property: JsonPropertyName("items")] IReadOnlyList<ProductionCommandResponse> Items);

public sealed record MesSyncStatusDto(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("realtime_snapshots_enabled")] bool RealtimeSnapshotsEnabled,
    [property: JsonPropertyName("machine_state_events_enabled")] bool MachineStateEventsEnabled,
    [property: JsonPropertyName("production_metrics_enabled")] bool ProductionMetricsEnabled,
    [property: JsonPropertyName("last_success_unix_seconds")] long? LastSuccessUnixTimeSeconds,
    [property: JsonPropertyName("last_error")] string? LastError,
    [property: JsonPropertyName("last_item_count")] int LastItemCount,
    [property: JsonPropertyName("dropped_batch_count")] long DroppedBatchCount);

public sealed record RealtimeSnapshotBatchPayload(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("gateway_id")] string GatewayId,
    [property: JsonPropertyName("created_at")] long CreatedAt,
    [property: JsonPropertyName("items")] IReadOnlyList<RealtimeSnapshotItemPayload> Items);

public sealed record RealtimeSnapshotItemPayload(
    [property: JsonPropertyName("machine_code")] string MachineCode,
    [property: JsonPropertyName("order_id")] string? OrderId,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("product_code")] string? ProductCode,
    [property: JsonPropertyName("mold_code")] string? MoldCode,
    [property: JsonPropertyName("machine_state")] string MachineState,
    [property: JsonPropertyName("actual_qty")] long ActualQty,
    [property: JsonPropertyName("total_qty")] long TotalQty,
    [property: JsonPropertyName("planned_qty")] decimal PlannedQty,
    [property: JsonPropertyName("availability")] decimal Availability,
    [property: JsonPropertyName("performance")] decimal Performance,
    [property: JsonPropertyName("quality")] decimal Quality,
    [property: JsonPropertyName("oee")] decimal Oee,
    [property: JsonPropertyName("extra")] JsonElement Extra);

public sealed record RealtimeSnapshotBuildResult(
    RealtimeSnapshotBatchPayload Payload,
    int RawIntervalCount,
    int SkippedItemCount);

public sealed record MachineStateEventBatchPayload(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("gateway_id")] string GatewayId,
    [property: JsonPropertyName("created_at")] long CreatedAt,
    [property: JsonPropertyName("items")] IReadOnlyList<MachineStateEventItemPayload> Items);

public sealed record MachineStateEventItemPayload(
    [property: JsonPropertyName("event_id")] string EventId,
    [property: JsonPropertyName("machine_code")] string MachineCode,
    [property: JsonPropertyName("order_id")] string OrderId,
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("start_at")] long StartAt,
    [property: JsonPropertyName("end_at")] long EndAt,
    [property: JsonPropertyName("duration_sec")] long DurationSec,
    [property: JsonPropertyName("is_open")] bool IsOpen);

public sealed record MachineStateEventBuildResult(
    IReadOnlyList<Rostek.Gateway.Domain.Entities.MachineStateEvent> Events,
    int SkippedItemCount);

public sealed record ProductionMetricItemPayload(
    [property: JsonPropertyName("metric_id")] string MetricId,
    [property: JsonPropertyName("bucket_type")] string BucketType,
    [property: JsonPropertyName("bucket_start")] long BucketStart,
    [property: JsonPropertyName("bucket_end")] long BucketEnd,
    [property: JsonPropertyName("machine_code")] string MachineCode,
    [property: JsonPropertyName("order_id")] string OrderId,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("product_code")] string ProductCode,
    [property: JsonPropertyName("mold_code")] string? MoldCode,
    [property: JsonPropertyName("machine_state")] string MachineState,
    [property: JsonPropertyName("actual_qty")] long ActualQty,
    [property: JsonPropertyName("total_qty")] long TotalQty,
    [property: JsonPropertyName("planned_qty")] decimal PlannedQty,
    [property: JsonPropertyName("target_qty")] int TargetQty,
    [property: JsonPropertyName("run_time")] long RunTime,
    [property: JsonPropertyName("stop_time")] long StopTime,
    [property: JsonPropertyName("error_time")] long ErrorTime,
    [property: JsonPropertyName("production_time")] long ProductionTime,
    [property: JsonPropertyName("availability")] decimal Availability,
    [property: JsonPropertyName("performance")] decimal Performance,
    [property: JsonPropertyName("quality")] decimal Quality,
    [property: JsonPropertyName("oee")] decimal Oee,
    [property: JsonPropertyName("is_final")] bool IsFinal,
    [property: JsonPropertyName("extra")] JsonElement Extra);

public sealed record ProductionMetricBuildResult(
    IReadOnlyList<Rostek.Gateway.Domain.Entities.ProductionMetric> Metrics,
    int SkippedItemCount);

public sealed record OeeLocalProcessingResult(
    RealtimeSnapshotBuildResult RealtimeSnapshot,
    MachineStateEventBuildResult MachineStateEvents,
    ProductionMetricBuildResult ProductionMetrics,
    int EnqueuedOutboxCount);

public sealed record SyncOutboxDispatchResult(
    int SentCount,
    int FailedCount);

public sealed record RealtimeSnapshotSyncStatus(
    long? LastSuccessUnixTimeSeconds,
    string? LastError,
    int LastItemCount,
    long DroppedBatchCount);

public interface IProductionCommandService
{
    Task<ProductionCommandBatchResponse> HandleBatchAsync(ProductionCommandBatchRequest request, CancellationToken cancellationToken);
    Task<ProductionCommandResponse> HandleAsync(ProductionCommandRequest request, CancellationToken cancellationToken);
}

public interface IRealtimeSnapshotBuilder
{
    Task<RealtimeSnapshotBuildResult> BuildAsync(
        string gatewayId,
        IReadOnlyCollection<Rostek.Gateway.Domain.Entities.PlcRawInterval> rawIntervals,
        long createdAt,
        CancellationToken cancellationToken);
}

public interface IRealtimeSnapshotClient
{
    Task SendAsync(RealtimeSnapshotBatchPayload payload, CancellationToken cancellationToken);
}

public interface IRealtimeSnapshotSyncStatusStore
{
    RealtimeSnapshotSyncStatus Current { get; }
    void MarkSuccess(long unixTimeSeconds, int itemCount);
    void MarkDropped(string error);
}

public interface IRealtimeSnapshotSyncService
{
    Task<RealtimeSnapshotBuildResult> SyncAsync(string gatewayId, CancellationToken cancellationToken);
}

public interface IMachineStateEventBuilder
{
    Task<MachineStateEventBuildResult> BuildAsync(
        string gatewayId,
        IReadOnlyCollection<Rostek.Gateway.Domain.Entities.PlcRawInterval> rawIntervals,
        long createdAt,
        CancellationToken cancellationToken);
}

public interface IProductionMetricBuilder
{
    Task<ProductionMetricBuildResult> BuildAsync(
        string gatewayId,
        IReadOnlyCollection<Rostek.Gateway.Domain.Entities.PlcRawInterval> rawIntervals,
        long createdAt,
        CancellationToken cancellationToken);

    Task<Rostek.Gateway.Domain.Entities.ProductionMetric?> BuildFinalSessionAsync(
        string gatewayId,
        Rostek.Gateway.Domain.Entities.ProductionContext context,
        long stoppedAt,
        CancellationToken cancellationToken);
}

public interface IOeeLocalProcessingService
{
    Task<OeeLocalProcessingResult> ProcessAsync(
        string gatewayId,
        IReadOnlyCollection<Rostek.Gateway.Domain.Entities.PlcRawInterval> rawIntervals,
        long createdAt,
        CancellationToken cancellationToken);
}

public interface ISyncOutboxDispatcher
{
    Task<SyncOutboxDispatchResult> DispatchPendingAsync(
        string gatewayId,
        IReadOnlyCollection<string> topics,
        CancellationToken cancellationToken);
}

public interface ISyncOutboxHttpClient
{
    Task SendAsync(string endpointPath, string payloadJson, CancellationToken cancellationToken);
}

public static class SyncOutboxStatuses
{
    public const string Pending = "pending";
    public const string Synced = "synced";
    public const string Failed = "failed";
}

public static class SyncOutboxTopics
{
    public const string RealtimeSnapshot = "realtime_snapshot";
    public const string MachineStateEvent = "machine_state_event";
    public const string ProductionMetric = "production_metric";
}

public static class MesSyncEndpointPaths
{
    public const string RealtimeSnapshots = "/api/v1/gateway/oee/realtime-snapshots";
    public const string MachineStateEvents = "/api/v1/gateway/oee/machine-state-events";
    public const string ProductionMetrics = "/api/v1/gateway/oee/production-metrics";
}

public static class ProductionCommandActions
{
    public const string Start = "start";
    public const string Pause = "pause";
    public const string Stop = "stop";

    public static bool TryMapStatus(string action, out string status)
    {
        status = action.Trim().ToLowerInvariant() switch
        {
            Start => "active",
            Pause => "pause",
            Stop => "stopped",
            _ => string.Empty
        };

        return status.Length > 0;
    }
}
