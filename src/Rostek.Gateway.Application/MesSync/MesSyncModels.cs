using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rostek.Gateway.Application.MesSync;

public sealed class MesSyncOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string BearerToken { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
    public int BatchSize { get; set; } = 100;
    public int SyncIntervalMs { get; set; } = 5000;
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

public sealed class ProductionCommandProduct
{
    [JsonPropertyName("product_code")]
    public string ProductCode { get; init; } = string.Empty;

    [JsonPropertyName("product_name")]
    public string? ProductName { get; init; }

    [JsonPropertyName("mold_code")]
    public string? MoldCode { get; init; }

    [JsonPropertyName("cavity")]
    public decimal Cavity { get; init; }

    [JsonPropertyName("cycle_time_seconds")]
    public decimal CycleTimeSeconds { get; init; }

    [JsonPropertyName("target_qty")]
    public int TargetQty { get; init; }

    [JsonPropertyName("extra")]
    public JsonElement? Extra { get; init; }
}

public sealed record ProductionCommandResponse(
    [property: JsonPropertyName("accepted")] bool Accepted,
    [property: JsonPropertyName("machine_code")] string MachineCode,
    [property: JsonPropertyName("command_code")] string CommandCode,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("production_order_code")] string? ProductionOrderCode,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("message")] string? Message);

public sealed record MesSyncStatusDto(
    [property: JsonPropertyName("enabled")] bool Enabled,
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
    [property: JsonPropertyName("_key")] string Key,
    [property: JsonPropertyName("machine_code")] string MachineCode,
    [property: JsonPropertyName("order_code")] string OrderCode,
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("product_code")] string ProductCode,
    [property: JsonPropertyName("mold_code")] string? MoldCode,
    [property: JsonPropertyName("machine_state")] string MachineState,
    [property: JsonPropertyName("good_qty")] long GoodQty,
    [property: JsonPropertyName("ng_qty")] long NgQty,
    [property: JsonPropertyName("actual_qty")] long ActualQty,
    [property: JsonPropertyName("planned_qty")] decimal PlannedQty,
    [property: JsonPropertyName("run_time")] long RunTime,
    [property: JsonPropertyName("stop_time")] long StopTime,
    [property: JsonPropertyName("error_time")] long ErrorTime,
    [property: JsonPropertyName("production_time")] long ProductionTime,
    [property: JsonPropertyName("availability")] decimal Availability,
    [property: JsonPropertyName("performance")] decimal Performance,
    [property: JsonPropertyName("quality")] decimal Quality,
    [property: JsonPropertyName("oee")] decimal Oee,
    [property: JsonPropertyName("extra")] JsonElement Extra);

public sealed record RealtimeSnapshotBuildResult(
    RealtimeSnapshotBatchPayload Payload,
    int RawIntervalCount,
    int SkippedItemCount);

public sealed record RealtimeSnapshotSyncStatus(
    long? LastSuccessUnixTimeSeconds,
    string? LastError,
    int LastItemCount,
    long DroppedBatchCount);

public interface IProductionCommandService
{
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
