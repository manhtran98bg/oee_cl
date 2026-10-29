using System.Text.Json;
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
    public const string OrderId = "TEST_ORDER";
    public const string ServerOrderId = "TEST_SERVER_ORDER";
    public const string PeriodId = "TEST_SESSION";
    public const int PlcPeriodIndex = 1;
    public const string ProductsJson = """[{"product_id":"TEST_PRODUCT","gain":1.0,"cycle_time":1.0,"target":0}]""";
    public const string ExtraJson = "{}";
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

public static class QuantityUpdateSources
{
    public const string Oi = "oi";
    public const string Oee = "oee";

    public static bool TryNormalize(string? value, out string source)
    {
        source = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return source is Oi or Oee;
    }
}

public sealed class OeeProductDefinition
{
    [JsonPropertyName("product_id")]
    public string ProductId { get; init; } = string.Empty;

    [JsonPropertyName("product_name")]
    public string? ProductName { get; init; }

    [JsonPropertyName("mold_code")]
    public string? MoldCode { get; init; }

    [JsonPropertyName("gain")]
    public decimal Gain { get; init; } = 1m;

    [JsonPropertyName("cycle_time")]
    public decimal CycleTime { get; init; }

    [JsonPropertyName("cycle")]
    public decimal LegacyCycle { get; init; }

    [JsonPropertyName("target")]
    public int Target { get; init; }

    [JsonPropertyName("quantity_update_source")]
    public string QuantityUpdateSource { get; set; } = QuantityUpdateSources.Oee;

    [JsonPropertyName("oee_accumulated_shot_ok")]
    public long OeeAccumulatedShotOk { get; set; }

    [JsonPropertyName("oee_accumulated_shot_ng")]
    public long OeeAccumulatedShotNg { get; set; }

    [JsonPropertyName("oee_accumulated_run_time_sec")]
    public long OeeAccumulatedRunTimeSec { get; set; }

    [JsonPropertyName("oee_accumulated_stop_time_sec")]
    public long OeeAccumulatedStopTimeSec { get; set; }

    [JsonPropertyName("oee_accumulated_error_time_sec")]
    public long OeeAccumulatedErrorTimeSec { get; set; }

    [JsonPropertyName("oee_accumulated_production_time_sec")]
    public long OeeAccumulatedProductionTimeSec { get; set; }

    [JsonPropertyName("oee_baseline_shot_ok_total")]
    public long? OeeBaselineShotOkTotal { get; set; }

    [JsonPropertyName("oee_baseline_shot_ng_total")]
    public long? OeeBaselineShotNgTotal { get; set; }

    [JsonPropertyName("oee_baseline_captured_at")]
    public long? OeeBaselineCapturedAt { get; set; }

    [JsonPropertyName("oee_baseline_run_time_total_sec")]
    public long? OeeBaselineRunTimeTotalSec { get; set; }

    [JsonPropertyName("oee_baseline_stop_time_total_sec")]
    public long? OeeBaselineStopTimeTotalSec { get; set; }

    [JsonPropertyName("oee_baseline_error_time_total_sec")]
    public long? OeeBaselineErrorTimeTotalSec { get; set; }

    [JsonPropertyName("oee_segment_started_at")]
    public long? OeeSegmentStartedAt { get; set; }

    [JsonPropertyName("oee_resumed_at")]
    public long? OeeResumedAt { get; set; }

    [JsonPropertyName("extra")]
    public JsonElement? Extra { get; init; }

    [JsonIgnore]
    public decimal EffectiveCycleTime => CycleTime > 0 ? CycleTime : LegacyCycle;

    [JsonIgnore]
    public bool IsOeeQuantitySource =>
        !QuantityUpdateSource.Equals(QuantityUpdateSources.Oi, StringComparison.OrdinalIgnoreCase);
}

public interface IRawDataCaptureService
{
    Task<IReadOnlyList<PlcRawInterval>> CaptureAsync(
        TimeSpan interval,
        bool requireProductionContext,
        CancellationToken cancellationToken);
}

public sealed record GatewayOeeTimerTotals(
    long RunTimeTotalSec,
    long StopTimeTotalSec,
    long ErrorTimeTotalSec);

public interface IGatewayOeeTimerService
{
    Task<GatewayOeeTimerTotals> CalculateAsync(
        string machineCode,
        string runState,
        long readAt,
        long maxElapsedSeconds,
        Func<CancellationToken, Task<PlcRawInterval?>> loadLatestRawAsync,
        CancellationToken cancellationToken);

    void Reset(string machineCode);
    void LogAutoFallbackOnce(string machineCode);
}
