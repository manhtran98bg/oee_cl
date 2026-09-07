using System.Text.Json.Serialization;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;

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

public static class MesSyncTopics
{
    public const string MetricSecond = "metric.second";
    public const string MetricState = "metric.state";
    public const string MetricHour = "metric.hour";
    public const string MetricDay = "metric.day";
    public const string MetricPeriod = "metric.period";
    public const string ProductMetric = "product_metric";
    public const string Downtime = "downtime";
}

public sealed record ProductionCommandRequest(
    [property: JsonPropertyName("machine_code")] string MachineCode,
    [property: JsonPropertyName("command_code")] string CommandCode,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("occurred_at_unix_seconds")] long? OccurredAtUnixTimeSeconds,
    [property: JsonPropertyName("production_order_code")] string? ProductionOrderCode,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("operator_code")] string? OperatorCode,
    [property: JsonPropertyName("reason_code")] string? ReasonCode,
    [property: JsonPropertyName("note")] string? Note);

public sealed record ProductionCommandResponse(
    [property: JsonPropertyName("accepted")] bool Accepted,
    [property: JsonPropertyName("machine_code")] string MachineCode,
    [property: JsonPropertyName("command_code")] string CommandCode,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("message")] string? Message);

public sealed record MesSyncStatusDto(
    bool Enabled,
    int PendingCount,
    int FailedCount,
    long? LastSuccessUnixTimeSeconds,
    string? LastError);

public interface IProductionCommandService
{
    Task<ProductionCommandResponse> HandleAsync(ProductionCommandRequest request, CancellationToken cancellationToken);
}

public interface IMesSyncOutboxService
{
    Task<MesOutboxBuildResult> EnqueueLocalOeeAsync(string gatewayId, CancellationToken cancellationToken);
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
