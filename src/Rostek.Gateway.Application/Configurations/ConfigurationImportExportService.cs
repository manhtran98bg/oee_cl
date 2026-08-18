using Microsoft.Extensions.Logging;
using Rostek.Gateway.Application.MachineGroups;
using Rostek.Gateway.Application.Machines;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.ImportExport;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Configurations;

public sealed class ConfigurationImportExportService(
    IConfigurationBuilder builder,
    IConfigurationVersionService versionService,
    IConfigRepository repository,
    IMachineGroupService groupService,
    IMachineService machineService,
    ILogger<ConfigurationImportExportService> logger) : IConfigurationImportExportService
{
    public async Task<string> ExportActiveJsonAsync(CancellationToken cancellationToken)
    {
        var active = await versionService.GetActiveAsync(cancellationToken);
        logger.LogInformation(
            "Exported active configuration. Version={Version}, MachineCount={MachineCount}",
            active?.Version ?? 0,
            active?.Machines.Count ?? 0);
        return ConfigurationJson.Serialize(active ?? RuntimeConfiguration.Empty);
    }

    public async Task<string> ExportDraftJsonAsync(CancellationToken cancellationToken)
    {
        var draft = await builder.BuildDraftAsync(cancellationToken);
        logger.LogInformation("Exported draft configuration. MachineCount={MachineCount}", draft.Machines.Count);
        return ConfigurationJson.Serialize(draft);
    }

    public Task<ImportPreview> PreviewJsonAsync(string json, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var configuration = ConfigurationJson.Deserialize(json);
            var items = configuration.Machines.Values
                .Select(machine => new ImportPreviewItem("Preview", "Machine", machine.MachineCode, "Machine found in JSON snapshot."))
                .ToList();
            logger.LogInformation("Previewed JSON configuration import. MachineCount={MachineCount}", items.Count);
            return Task.FromResult(new ImportPreview(true, items, [], "json"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "JSON configuration import preview failed");
            return Task.FromResult(new ImportPreview(false, [], [ex.Message], "json"));
        }
    }

    public async Task<ImportPreview> PreviewMachinesCsvAsync(string csv, CancellationToken cancellationToken)
    {
        var rows = ParseCsv(csv);
        var errors = new List<string>();
        var items = new List<ImportPreviewItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(row.MachineCode))
            {
                errors.Add($"Duplicate machine code in CSV: {row.MachineCode}");
            }

            var template = (await repository.ListTemplatesAsync(false, cancellationToken)).FirstOrDefault(item => item.Code.Equals(row.TemplateCode, StringComparison.OrdinalIgnoreCase));
            var group = string.IsNullOrWhiteSpace(row.GroupCode) ? null : (await repository.ListGroupsAsync(cancellationToken)).FirstOrDefault(item => item.Code.Equals(row.GroupCode, StringComparison.OrdinalIgnoreCase));
            var existing = await repository.GetMachineByCodeAsync(row.MachineCode, includeDetails: false, cancellationToken);
            var action = existing is null ? "Create" : "Update";
            var message = template is null ? "Template missing." : group is null && !string.IsNullOrWhiteSpace(row.GroupCode) ? "Group missing." : "Ready.";
            if (template is null)
            {
                errors.Add($"Template '{row.TemplateCode}' not found for machine '{row.MachineCode}'.");
            }

            items.Add(new ImportPreviewItem(action, "Machine", row.MachineCode, message));
        }

        logger.LogInformation("Previewed machine CSV import. ItemCount={ItemCount}, ErrorCount={ErrorCount}", items.Count, errors.Count);
        return new ImportPreview(errors.Count == 0, items, errors, "csv");
    }

    public async Task<ImportPreview> CommitMachinesCsvAsync(string csv, bool createMissingGroups, bool updateExisting, string? userName, CancellationToken cancellationToken)
    {
        var preview = await PreviewMachinesCsvAsync(csv, cancellationToken);
        if (!preview.IsValid)
        {
            return preview;
        }

        foreach (var row in ParseCsv(csv))
        {
            var groups = await repository.ListGroupsAsync(cancellationToken);
            var group = groups.FirstOrDefault(item => item.Code.Equals(row.GroupCode, StringComparison.OrdinalIgnoreCase));
            if (group is null && createMissingGroups && !string.IsNullOrWhiteSpace(row.GroupCode))
            {
                var result = await groupService.SaveAsync(new MachineGroupInput { Code = row.GroupCode, Name = row.GroupCode }, userName, cancellationToken);
                group = await repository.GetGroupAsync(result.Value, cancellationToken);
            }

            var template = (await repository.ListTemplatesAsync(false, cancellationToken)).First(item => item.Code.Equals(row.TemplateCode, StringComparison.OrdinalIgnoreCase));
            var existing = await repository.GetMachineByCodeAsync(row.MachineCode, includeDetails: false, cancellationToken);
            if (existing is not null && !updateExisting)
            {
                continue;
            }

            var input = new MachineEditInput
            {
                Id = existing?.Id,
                Code = row.MachineCode,
                Name = row.Name,
                GroupId = group?.Id,
                TemplateId = template.Id,
                Enabled = row.Enabled
            };

            if (template.Protocol.ToString().Equals("OpcUa", StringComparison.OrdinalIgnoreCase))
            {
                input.OpcUa.EndpointUrl = row.EndpointUrl;
            }
            else
            {
                input.ModbusTcp.Host = row.Host;
                input.ModbusTcp.Port = row.Port;
                input.ModbusTcp.UnitId = row.UnitId;
            }

            var save = await machineService.SaveAsync(input, userName, cancellationToken);
            if (!save.Succeeded)
            {
                throw new InvalidOperationException(save.ErrorMessage);
            }
        }

        logger.LogInformation("Committed machine CSV import. RowCount={RowCount}, CreateMissingGroups={CreateMissingGroups}, UpdateExisting={UpdateExisting}", ParseCsv(csv).Count, createMissingGroups, updateExisting);
        return await PreviewMachinesCsvAsync(csv, cancellationToken);
    }

    private static List<MachineCsvRow> ParseCsv(string csv)
    {
        var lines = csv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2)
        {
            return [];
        }

        var rows = new List<MachineCsvRow>();
        foreach (var line in lines.Skip(1))
        {
            var columns = line.Split(',');
            if (columns.Length < 9)
            {
                throw new InvalidOperationException("CSV must contain machineCode,name,groupCode,templateCode,host,port,unitId,endpointUrl,enabled.");
            }

            rows.Add(new MachineCsvRow(
                columns[0].Trim().ToUpperInvariant(),
                columns[1].Trim(),
                columns[2].Trim().ToUpperInvariant(),
                columns[3].Trim().ToUpperInvariant(),
                EmptyToNull(columns[4]),
                int.TryParse(columns[5], out var port) ? port : null,
                int.TryParse(columns[6], out var unitId) ? unitId : null,
                EmptyToNull(columns[7]),
                bool.TryParse(columns[8], out var enabled) && enabled));
        }

        return rows;
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record MachineCsvRow(string MachineCode, string Name, string GroupCode, string TemplateCode, string? Host, int? Port, int? UnitId, string? EndpointUrl, bool Enabled);
}
