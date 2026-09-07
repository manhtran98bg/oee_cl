using System.Text.Json.Serialization;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Oee;

public static class OeeSignalCodes
{
    public const string MachineState = "MACHINE_STATE";
    public const string ShotOkCount = "SHOT_OK_COUNT";
    public const string ShotNgCount = "SHOT_NG_COUNT";
    public const string CycleTimeMs = "CYCLE_TIME_MS";
    public const string RunTimeTotal = "RUN_TIME_TOTAL";
    public const string StopTimeTotal = "STOP_TIME_TOTAL";
    public const string ErrorTimeTotal = "ERROR_TIME_TOTAL";
}

public static class OeeTestProductionContext
{
    public const string Mode = "production";
    public const string OrderId = "TEST_ORDER";
    public const string ServerOrderId = "TEST_SERVER_ORDER";
    public const string PeriodId = "TEST_SESSION";
    public const int PlcPeriodIndex = 1;
    public const string ProductsJson = """[{"product_id":"TEST_PRODUCT","gain":1.0,"cycle_time":1.0,"target":0}]""";
    public const string TagsJson = "[]";
    public const string ExtraJson = "{}";
}

public static class OeeMetricTypes
{
    public const string Second = "second";
    public const string State = "state";
    public const string Period = "period";
    public const string Hour = "hour";
    public const string Day = "day";
}

public static class OeeRunStates
{
    public const string Run = "run";
    public const string Stop = "stop";
    public const string Error = "error";
    public const string Disconnect = "disconnect";

    public static bool IsDowntime(string state) =>
        state.Equals(Stop, StringComparison.OrdinalIgnoreCase) ||
        state.Equals(Error, StringComparison.OrdinalIgnoreCase);

    public static bool IsKnownProcessState(string state) =>
        state.Equals(Run, StringComparison.OrdinalIgnoreCase) ||
        state.Equals(Stop, StringComparison.OrdinalIgnoreCase) ||
        state.Equals(Error, StringComparison.OrdinalIgnoreCase);
}

public sealed class OeeProductDefinition
{
    [JsonPropertyName("product_id")]
    public string ProductId { get; init; } = string.Empty;

    [JsonPropertyName("gain")]
    public decimal Gain { get; init; } = 1m;

    [JsonPropertyName("cycle_time")]
    public decimal CycleTime { get; init; }

    [JsonPropertyName("cycle")]
    public decimal LegacyCycle { get; init; }

    [JsonPropertyName("target")]
    public int Target { get; init; }

    [JsonIgnore]
    public decimal EffectiveCycleTime => CycleTime > 0 ? CycleTime : LegacyCycle;
}

public sealed record OeeBuildResult(
    IReadOnlyList<ProductionMetric> ProductionMetrics,
    IReadOnlyList<ProductMetric> ProductMetrics,
    IReadOnlyList<DowntimeEvent> DowntimeEvents)
{
    public int ProductionMetricCount => ProductionMetrics.Count;
    public int ProductMetricCount => ProductMetrics.Count;
    public int DowntimeEventCount => DowntimeEvents.Count;
}

public sealed record MesOutboxBuildResult(
    int RawIntervalCount,
    int ProductionMetricCount,
    int ProductMetricCount,
    int DowntimeEventCount,
    int OutboxCount);

public sealed record OeeMetricPayload(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("machine")] string Machine,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("period_id")] string PeriodId,
    [property: JsonPropertyName("order_id")] string OrderId,
    [property: JsonPropertyName("tag")] string Tag,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("run_time")] int RunTime,
    [property: JsonPropertyName("error_time")] int ErrorTime,
    [property: JsonPropertyName("stop_time")] int StopTime,
    [property: JsonPropertyName("prod_time")] int ProdTime,
    [property: JsonPropertyName("plan")] decimal Plan,
    [property: JsonPropertyName("A")] decimal Availability,
    [property: JsonPropertyName("P")] decimal Performance,
    [property: JsonPropertyName("Q")] decimal Quality,
    [property: JsonPropertyName("cycle")] decimal Cycle,
    [property: JsonPropertyName("OEE")] decimal Oee,
    [property: JsonPropertyName("product_id")] string ProductId,
    [property: JsonPropertyName("start_at")] long StartAt,
    [property: JsonPropertyName("end_at")] long EndAt,
    [property: JsonPropertyName("updated_at")] long UpdatedAt,
    [property: JsonPropertyName("count_check")] int? CountCheck,
    [property: JsonPropertyName("ng")] int? Ng,
    [property: JsonPropertyName("_sync_target")] string SyncTarget);

public interface IOeeLocalRepository
{
    Task<ProductionContext?> GetProductionContextAsync(string machine, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, ProductionContext>> ListProductionContextsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, ProductionContext>> ListActiveProductionContextsAsync(CancellationToken cancellationToken);
    Task<ProductionContext> EnsureTestProductionContextAsync(string machine, long startAt, CancellationToken cancellationToken);
    Task SaveProductionContextAsync(ProductionContext context, CancellationToken cancellationToken);
    Task<ProductionPeriod?> GetProductionPeriodAsync(string periodId, CancellationToken cancellationToken);
    Task<ProductionPeriod> EnsureTestProductionPeriodAsync(ProductionContext context, long startAt, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlcRawInterval>> InsertMissingRawIntervalsAsync(IReadOnlyCollection<PlcRawInterval> rawIntervals, CancellationToken cancellationToken);
    Task<PlcRawInterval?> GetPreviousRawInPeriodAsync(string machine, int plcPeriodIndex, long periodStartAt, long beforeReadAt, CancellationToken cancellationToken);
    Task<PlcRawInterval?> GetFirstRawInRangeAsync(string machine, int plcPeriodIndex, long startAt, long beforeReadAt, CancellationToken cancellationToken);
    Task<ProductionMetric?> GetProductionMetricAsync(string metricType, string periodId, string productId, string runState, long startAt, CancellationToken cancellationToken);
    Task<ProductionMetric?> GetLatestStateMetricAsync(string machine, string periodId, string productId, string runState, long beforeStartAt, CancellationToken cancellationToken);
    Task UpsertProductionMetricAsync(ProductionMetric metric, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductionMetric>> ListPeriodMetricsAsync(string orderId, string productId, CancellationToken cancellationToken);
    Task UpsertProductMetricAsync(ProductMetric metric, CancellationToken cancellationToken);
    Task<DowntimeEvent?> GetOpenDowntimeEventAsync(string machine, string periodId, CancellationToken cancellationToken);
    Task UpsertDowntimeEventAsync(DowntimeEvent downtimeEvent, CancellationToken cancellationToken);
    Task EnqueueOutboxAsync(MesSyncOutboxMessage message, CancellationToken cancellationToken);
    Task<IReadOnlyList<MesSyncOutboxMessage>> TakePendingOutboxAsync(int batchSize, CancellationToken cancellationToken);
    Task MarkOutboxSyncedAsync(string id, long now, CancellationToken cancellationToken);
    Task MarkOutboxFailedAsync(string id, string error, long now, CancellationToken cancellationToken);
    Task<(int PendingCount, int FailedCount, long? LastSuccess, string? LastError)> GetOutboxStatusAsync(CancellationToken cancellationToken);
}
