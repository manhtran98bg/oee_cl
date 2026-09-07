using System.Text.Json.Serialization;

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
    public const string CommandCode = "TEST";
    public const string ProductionOrderCode = "TEST_ORDER";
    public const string SessionId = "TEST_SESSION";
}

public sealed record OeeRawSample(
    string MachineCode,
    string ProductionOrderCode,
    string SessionId,
    long SampledAtUnixTimeSeconds,
    int? MachineState,
    long? ShotOkCount,
    long? ShotNgCount,
    int? CycleTimeMs,
    long? RunTimeTotal,
    long? StopTimeTotal,
    long? ErrorTimeTotal);

public sealed record OeeDeltaSample(
    string MachineCode,
    string ProductionOrderCode,
    string SessionId,
    long SampledAtUnixTimeSeconds,
    int? MachineState,
    int? ShotOkDelta,
    int? ShotNgDelta,
    int? CycleTimeMs,
    int? RunTimeDeltaSeconds,
    int? StopTimeDeltaSeconds,
    int? ErrorTimeDeltaSeconds);

public sealed record OeeMetric(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("machine")] string Machine,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("order_id")] string? OrderId,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("tag")] string? Tag,
    [property: JsonPropertyName("total")] int? Total,
    [property: JsonPropertyName("ng_qty")] int? NgQty,
    [property: JsonPropertyName("run_time")] decimal? RunTime,
    [property: JsonPropertyName("error_time")] decimal? ErrorTime,
    [property: JsonPropertyName("stop_time")] decimal? StopTime,
    [property: JsonPropertyName("prod_time")] decimal? ProdTime,
    [property: JsonPropertyName("A")] decimal? Availability,
    [property: JsonPropertyName("P")] decimal? Performance,
    [property: JsonPropertyName("Q")] decimal? Quality,
    [property: JsonPropertyName("cycle")] decimal? Cycle,
    [property: JsonPropertyName("OEE")] decimal? Oee,
    [property: JsonPropertyName("product_id")] string? ProductId,
    [property: JsonPropertyName("start_at")] long? StartAt,
    [property: JsonPropertyName("end_at")] long EndAt,
    [property: JsonPropertyName("updated_at")] long UpdatedAt);
