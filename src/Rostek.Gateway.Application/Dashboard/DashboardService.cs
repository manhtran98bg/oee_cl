using System.Globalization;
using Rostek.Gateway.Application.Machines;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Contracts.Machines;
using Rostek.Gateway.Contracts.Runtime;

namespace Rostek.Gateway.Application.Dashboard;

public sealed class DashboardService(
    IMachineService machineService,
    IRuntimeConfigurationProvider configurationProvider,
    IRuntimeStatusReader runtimeStatusReader,
    IMachineValueReader valueReader) : IDashboardService
{
    public async Task<DashboardDto> GetAsync(CancellationToken cancellationToken)
    {
        var machines = await machineService.ListAsync(new MachineQuery(null, null, null, null, null, 1, 200), cancellationToken);
        var statuses = runtimeStatusReader.GetStatuses().ToDictionary(status => status.MachineCode, StringComparer.OrdinalIgnoreCase);
        var snapshots = valueReader.GetSnapshots().ToDictionary(snapshot => snapshot.MachineCode, StringComparer.OrdinalIgnoreCase);

        var rows = machines.Items
            .OrderBy(machine => machine.Code, StringComparer.OrdinalIgnoreCase)
            .Select(machine =>
            {
                statuses.TryGetValue(machine.Code, out var status);
                snapshots.TryGetValue(machine.Code, out var snapshot);
                return CreateRow(machine, status, snapshot);
            })
            .ToList();

        var online = rows.Count(row => row.Online);
        var summary = new DashboardSummaryDto(
            machines.Items.Count,
            machines.Items.Count(machine => machine.Enabled),
            statuses.Count,
            online,
            statuses.Count - online,
            configurationProvider.Current.Version);

        return new DashboardDto(summary, rows);
    }

    private static MachineDashboardRowDto CreateRow(
        MachineListItem machine,
        MachineRuntimeStatusDto? status,
        MachineValueSnapshotDto? snapshot)
    {
        var runtimeState = status?.State.ToString() ?? (machine.Enabled ? "NotStarted" : "Disabled");
        var online = machine.Enabled && status?.State == MachineRuntimeState.Connected && snapshot?.Online == true;
        var updatedAtUtc = status?.UpdatedAtUtc ?? snapshot?.LastReadUtc;
        var message = status?.Message ?? (machine.Enabled ? "No runtime status" : "Machine disabled");
        var signals = CreateSignals(snapshot);
        var goodSignalCount = signals.Count(signal => signal.Quality == MachineReadQuality.Good.ToString());

        return new MachineDashboardRowDto(
            machine.Code,
            machine.Name,
            machine.Protocol.ToString(),
            machine.Enabled,
            machine.Endpoint,
            runtimeState,
            online,
            message,
            updatedAtUtc,
            status?.StartCount ?? 0,
            status?.StopCount ?? 0,
            signals.Count,
            goodSignalCount,
            signals.Count - goodSignalCount,
            signals);
    }

    private static IReadOnlyList<MachineSignalDashboardDto> CreateSignals(MachineValueSnapshotDto? snapshot) =>
        snapshot?.Values
            .OrderBy(value => value.SignalCode, StringComparer.OrdinalIgnoreCase)
            .Select(value => new MachineSignalDashboardDto(
                value.SignalCode,
                FormatValue(value.Value),
                value.DataType,
                value.Quality.ToString(),
                value.TimestampUtc,
                value.Error))
            .ToList() ?? [];

    private static string FormatValue(object? value) =>
        value switch
        {
            null => "-",
            string text => string.IsNullOrWhiteSpace(text) ? "-" : text,
            bool boolean => boolean ? "true" : "false",
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal =>
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? "-",
            _ => "[unsupported]"
        };
}
