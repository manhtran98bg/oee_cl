namespace Rostek.Gateway.Contracts.Machines;

public sealed record MachineValueSnapshotDto(
    string MachineCode,
    bool Online,
    DateTimeOffset? LastReadUtc,
    IReadOnlyList<SignalValueDto> Values);
