using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.UnitTests.Support;

public sealed class InMemoryConfigRepository(params string[] machineCodes) : IConfigRepository
{
    private readonly HashSet<string> _machineCodes = new(machineCodes, StringComparer.OrdinalIgnoreCase);

    public Task<Machine?> GetMachineByCodeAsync(string code, bool includeDetails, CancellationToken cancellationToken) =>
        Task.FromResult(_machineCodes.Contains(code) ? new Machine { Code = code, Name = code } : null);

    public Task<bool> MachineCodeExistsAsync(string code, Guid? exceptId, CancellationToken cancellationToken) =>
        Task.FromResult(_machineCodes.Contains(code));

    public Task<List<MachineGroup>> ListGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<MachineGroup?> GetGroupAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> GroupCodeExistsAsync(string code, Guid? exceptId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task AddGroupAsync(MachineGroup group, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<List<MachineTemplate>> ListTemplatesAsync(bool includeSignals, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<MachineTemplate?> GetTemplateAsync(Guid id, bool includeSignals, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> TemplateCodeExistsAsync(string code, Guid? exceptId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task AddTemplateAsync(MachineTemplate template, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<TemplateSignal?> GetTemplateSignalAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task AddTemplateSignalAsync(TemplateSignal signal, CancellationToken cancellationToken) => throw new NotSupportedException();
    public void RemoveTemplateSignal(TemplateSignal signal) => throw new NotSupportedException();
    public Task<List<Machine>> ListMachinesAsync(MachineQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<Machine?> GetMachineAsync(Guid id, bool includeDetails, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task AddMachineAsync(Machine machine, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task AddConnectionAsync(MachineConnection connection, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<MachineSignalOverride?> GetSignalOverrideAsync(Guid machineId, Guid templateSignalId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task AddSignalOverrideAsync(MachineSignalOverride signalOverride, CancellationToken cancellationToken) => throw new NotSupportedException();
    public void RemoveSignalOverride(MachineSignalOverride signalOverride) => throw new NotSupportedException();
    public Task<List<ConfigurationVersion>> ListVersionsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<ConfigurationVersion?> GetActiveVersionAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<ConfigurationVersion?> GetVersionAsync(long version, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<long> GetNextVersionNumberAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task AddConfigurationVersionAsync(ConfigurationVersion version, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task AddAuditLogAsync(AuditLog auditLog, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
