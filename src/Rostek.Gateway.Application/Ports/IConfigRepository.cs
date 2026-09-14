using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Ports;

public interface IConfigRepository
{
    Task<List<MachineGroup>> ListGroupsAsync(CancellationToken cancellationToken);
    Task<MachineGroup?> GetGroupAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> GroupCodeExistsAsync(string code, Guid? exceptId, CancellationToken cancellationToken);
    Task AddGroupAsync(MachineGroup group, CancellationToken cancellationToken);

    Task<List<MachineTemplate>> ListTemplatesAsync(bool includeSignals, CancellationToken cancellationToken);
    Task<MachineTemplate?> GetTemplateAsync(Guid id, bool includeSignals, CancellationToken cancellationToken);
    Task<bool> TemplateCodeExistsAsync(string code, Guid? exceptId, CancellationToken cancellationToken);
    Task AddTemplateAsync(MachineTemplate template, CancellationToken cancellationToken);
    Task<TemplateSignal?> GetTemplateSignalAsync(Guid id, CancellationToken cancellationToken);
    Task AddTemplateSignalAsync(TemplateSignal signal, CancellationToken cancellationToken);
    void RemoveTemplateSignal(TemplateSignal signal);

    Task<List<Machine>> ListMachinesAsync(MachineQuery query, CancellationToken cancellationToken);
    Task<Machine?> GetMachineAsync(Guid id, bool includeDetails, CancellationToken cancellationToken);
    Task<Machine?> GetMachineByCodeAsync(string code, bool includeDetails, CancellationToken cancellationToken);
    Task<bool> MachineCodeExistsAsync(string code, Guid? exceptId, CancellationToken cancellationToken);
    Task AddMachineAsync(Machine machine, CancellationToken cancellationToken);
    void RemoveMachine(Machine machine);
    Task AddConnectionAsync(MachineConnection connection, CancellationToken cancellationToken);
    Task<MachineSignalOverride?> GetSignalOverrideAsync(Guid machineId, Guid templateSignalId, CancellationToken cancellationToken);
    Task AddSignalOverrideAsync(MachineSignalOverride signalOverride, CancellationToken cancellationToken);
    void RemoveSignalOverride(MachineSignalOverride signalOverride);

    Task<List<ConfigurationVersion>> ListVersionsAsync(CancellationToken cancellationToken);
    Task<ConfigurationVersion?> GetActiveVersionAsync(CancellationToken cancellationToken);
    Task<ConfigurationVersion?> GetVersionAsync(long version, CancellationToken cancellationToken);
    Task<long> GetNextVersionNumberAsync(CancellationToken cancellationToken);
    Task AddConfigurationVersionAsync(ConfigurationVersion version, CancellationToken cancellationToken);

    Task AddAuditLogAsync(AuditLog auditLog, CancellationToken cancellationToken);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record MachineQuery(
    string? Search,
    Guid? GroupId,
    Guid? TemplateId,
    string? Protocol,
    bool? Enabled,
    int Page,
    int PageSize);
