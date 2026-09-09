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
    public const string OrderCode = "TEST_ORDER";
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

    [JsonPropertyName("extra")]
    public JsonElement? Extra { get; init; }

    [JsonIgnore]
    public decimal EffectiveCycleTime => CycleTime > 0 ? CycleTime : LegacyCycle;
}

public interface IRawDataCaptureService
{
    Task<IReadOnlyList<PlcRawInterval>> CaptureAsync(
        TimeSpan interval,
        bool requireProductionContext,
        CancellationToken cancellationToken);
}
