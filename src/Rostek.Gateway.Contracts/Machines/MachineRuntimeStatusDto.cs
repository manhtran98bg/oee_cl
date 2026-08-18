namespace Rostek.Gateway.Contracts.Machines;

public enum MachineRuntimeState
{
    Disabled,
    Stopped,
    Starting,
    Connecting,
    Connected,
    Degraded,
    Reconnecting,
    Faulted,
    Stopping
}

public sealed record MachineRuntimeStatusDto(
    string MachineCode,
    MachineRuntimeState State,
    string? Message,
    DateTimeOffset UpdatedAtUtc,
    int StartCount,
    int StopCount);
