using System.Text.Json.Serialization;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Domain.Enums;

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
}

public static class MesSyncTopics
{
    public const string MetricSecond = "metric.second";
}

public static class MesSyncEndpoints
{
    public const string SecondlyProductionSync = "/secondly-production/sync";
}

public sealed record ProductionCommandRequest(
    [property: JsonPropertyName("machine_code")] string MachineCode,
    [property: JsonPropertyName("command_code")] string CommandCode,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("occurred_at_unix_seconds")] long? OccurredAtUnixTimeSeconds,
    [property: JsonPropertyName("production_order_code")] string? ProductionOrderCode,
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
    Task<int> EnqueueSecondlyMetricsAsync(string gatewayId, CancellationToken cancellationToken);
}

public interface IMesSyncDispatcher
{
    Task<int> DispatchPendingAsync(CancellationToken cancellationToken);
}

public interface IMesServerClient
{
    Task SendAsync(string endpoint, string payloadJson, CancellationToken cancellationToken);
}

public interface IMesSyncOutboxRepository
{
    Task<ProductionContext?> GetProductionContextAsync(string machineCode, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, ProductionContext>> ListActiveProductionContextsAsync(CancellationToken cancellationToken);
    Task SaveProductionContextAsync(ProductionContext context, CancellationToken cancellationToken);
    Task AddOutboxMessageAsync(MesSyncOutboxMessage message, CancellationToken cancellationToken);
    Task<List<MesSyncOutboxMessage>> TakePendingAsync(long nowUnixTimeSeconds, int batchSize, CancellationToken cancellationToken);
    Task MarkSyncedAsync(long id, long nowUnixTimeSeconds, CancellationToken cancellationToken);
    Task MarkFailedAsync(long id, string error, long nextAttemptUnixTimeSeconds, long nowUnixTimeSeconds, CancellationToken cancellationToken);
    Task<MesSyncStatusDto> GetStatusAsync(bool enabled, CancellationToken cancellationToken);
}

public static class ProductionCommandActions
{
    public const string Start = "start";
    public const string Pause = "pause";
    public const string Stop = "stop";

    public static bool TryMapStatus(string action, out ProductionContextStatus status)
    {
        status = action.Trim().ToLowerInvariant() switch
        {
            Start => ProductionContextStatus.Started,
            Pause => ProductionContextStatus.Paused,
            Stop => ProductionContextStatus.Stopped,
            _ => default
        };

        return status != default;
    }
}
