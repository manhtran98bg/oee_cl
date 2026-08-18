namespace Rostek.Gateway.Application.Dashboard;

public sealed record DashboardDto(
    DashboardSummaryDto Summary,
    IReadOnlyList<MachineDashboardRowDto> Machines);

public sealed record DashboardSummaryDto(
    int Total,
    int Enabled,
    int Runtime,
    int Online,
    int Offline,
    long ActiveVersion);

public sealed record MachineDashboardRowDto(
    string MachineCode,
    string MachineName,
    string Protocol,
    bool Enabled,
    string Connection,
    string RuntimeState,
    bool Online,
    string? Message,
    DateTimeOffset? UpdatedAtUtc,
    int StartCount,
    int StopCount,
    int SignalCount,
    int GoodSignalCount,
    int BadSignalCount,
    IReadOnlyList<MachineSignalDashboardDto> Signals);

public sealed record MachineSignalDashboardDto(
    string SignalCode,
    string Value,
    string DataType,
    string Quality,
    DateTimeOffset? LastReadUtc,
    string? Error);
