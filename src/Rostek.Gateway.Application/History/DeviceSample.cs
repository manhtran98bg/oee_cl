namespace Rostek.Gateway.Application.History;

public sealed record DeviceSample(
    string GatewayId,
    string MachineCode,
    DateTimeOffset SampledAtUtc,
    DateTimeOffset CreatedAtUtc,
    short? MachineState,
    int? ShotOkDelta,
    int? ShotNgDelta,
    int? CycleTimeMs,
    int? RunTimeDeltaMs,
    int? StopTimeDeltaMs,
    int? ErrorTimeDeltaMs);
