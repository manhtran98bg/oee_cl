using Microsoft.EntityFrameworkCore;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Domain.Enums;
using Rostek.Gateway.Infrastructure.Persistence;

namespace Rostek.Gateway.Infrastructure.Repositories;

public sealed class EfCoreConfigRepository(GatewayDbContext dbContext) : IConfigRepository
{
    public Task<List<MachineGroup>> ListGroupsAsync(CancellationToken cancellationToken) =>
        dbContext.MachineGroups.AsNoTracking().OrderBy(group => group.Code).ToListAsync(cancellationToken);

    public Task<MachineGroup?> GetGroupAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.MachineGroups.FirstOrDefaultAsync(group => group.Id == id, cancellationToken);

    public Task<bool> GroupCodeExistsAsync(string code, Guid? exceptId, CancellationToken cancellationToken) =>
        dbContext.MachineGroups.AnyAsync(group => group.Code.ToUpper() == code.ToUpper() && group.Id != exceptId, cancellationToken);

    public Task AddGroupAsync(MachineGroup group, CancellationToken cancellationToken) =>
        dbContext.MachineGroups.AddAsync(group, cancellationToken).AsTask();

    public Task<List<MachineTemplate>> ListTemplatesAsync(bool includeSignals, CancellationToken cancellationToken)
    {
        IQueryable<MachineTemplate> query = dbContext.MachineTemplates;
        if (includeSignals)
        {
            query = query.Include(template => template.Signals);
        }

        return query.AsNoTracking().OrderBy(template => template.Code).ToListAsync(cancellationToken);
    }

    public Task<MachineTemplate?> GetTemplateAsync(Guid id, bool includeSignals, CancellationToken cancellationToken)
    {
        IQueryable<MachineTemplate> query = dbContext.MachineTemplates;
        if (includeSignals)
        {
            query = query.Include(template => template.Signals);
        }

        return query.FirstOrDefaultAsync(template => template.Id == id, cancellationToken);
    }

    public Task<bool> TemplateCodeExistsAsync(string code, Guid? exceptId, CancellationToken cancellationToken) =>
        dbContext.MachineTemplates.AnyAsync(template => template.Code.ToUpper() == code.ToUpper() && template.Id != exceptId, cancellationToken);

    public Task AddTemplateAsync(MachineTemplate template, CancellationToken cancellationToken) =>
        dbContext.MachineTemplates.AddAsync(template, cancellationToken).AsTask();

    public Task<TemplateSignal?> GetTemplateSignalAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.TemplateSignals.FirstOrDefaultAsync(signal => signal.Id == id, cancellationToken);

    public Task AddTemplateSignalAsync(TemplateSignal signal, CancellationToken cancellationToken) =>
        dbContext.TemplateSignals.AddAsync(signal, cancellationToken).AsTask();

    public void RemoveTemplateSignal(TemplateSignal signal) => dbContext.TemplateSignals.Remove(signal);

    public Task<List<Machine>> ListMachinesAsync(MachineQuery query, CancellationToken cancellationToken)
    {
        IQueryable<Machine> machines = dbContext.Machines
            .Include(machine => machine.Group)
            .Include(machine => machine.Template)
            .ThenInclude(template => template!.Signals)
            .Include(machine => machine.Connection)
            .Include(machine => machine.SignalOverrides);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToUpperInvariant();
            machines = machines.Where(machine => machine.Code.ToUpper().Contains(search) || machine.Name.ToUpper().Contains(search));
        }

        if (query.GroupId is Guid groupId)
        {
            machines = machines.Where(machine => machine.GroupId == groupId);
        }

        if (query.TemplateId is Guid templateId)
        {
            machines = machines.Where(machine => machine.TemplateId == templateId);
        }

        if (query.Protocol is { Length: > 0 } protocol && Enum.TryParse<GatewayProtocol>(protocol, ignoreCase: true, out var parsedProtocol))
        {
            machines = machines.Where(machine => machine.Template != null && machine.Template.Protocol == parsedProtocol);
        }

        if (query.Enabled is bool enabled)
        {
            machines = machines.Where(machine => machine.Enabled == enabled);
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        return machines
            .OrderBy(machine => machine.DisplayOrder)
            .ThenBy(machine => machine.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public Task<Machine?> GetMachineAsync(Guid id, bool includeDetails, CancellationToken cancellationToken)
    {
        IQueryable<Machine> query = dbContext.Machines;
        if (includeDetails)
        {
            query = query
                .Include(machine => machine.Group)
                .Include(machine => machine.Template)
                .ThenInclude(template => template!.Signals)
                .Include(machine => machine.Connection)
                .Include(machine => machine.SignalOverrides);
        }

        return query.FirstOrDefaultAsync(machine => machine.Id == id, cancellationToken);
    }

    public Task<Machine?> GetMachineByCodeAsync(string code, bool includeDetails, CancellationToken cancellationToken)
    {
        IQueryable<Machine> query = dbContext.Machines;
        if (includeDetails)
        {
            query = query.Include(machine => machine.Template).ThenInclude(template => template!.Signals).Include(machine => machine.Connection).Include(machine => machine.SignalOverrides);
        }

        return query.FirstOrDefaultAsync(machine => machine.Code.ToUpper() == code.ToUpper(), cancellationToken);
    }

    public Task<bool> MachineCodeExistsAsync(string code, Guid? exceptId, CancellationToken cancellationToken) =>
        dbContext.Machines.AnyAsync(machine => machine.Code.ToUpper() == code.ToUpper() && machine.Id != exceptId, cancellationToken);

    public Task AddMachineAsync(Machine machine, CancellationToken cancellationToken) =>
        dbContext.Machines.AddAsync(machine, cancellationToken).AsTask();

    public void RemoveMachine(Machine machine) => dbContext.Machines.Remove(machine);

    public Task AddConnectionAsync(MachineConnection connection, CancellationToken cancellationToken) =>
        dbContext.MachineConnections.AddAsync(connection, cancellationToken).AsTask();

    public Task<MachineSignalOverride?> GetSignalOverrideAsync(Guid machineId, Guid templateSignalId, CancellationToken cancellationToken) =>
        dbContext.MachineSignalOverrides.FirstOrDefaultAsync(item => item.MachineId == machineId && item.TemplateSignalId == templateSignalId, cancellationToken);

    public Task AddSignalOverrideAsync(MachineSignalOverride signalOverride, CancellationToken cancellationToken) =>
        dbContext.MachineSignalOverrides.AddAsync(signalOverride, cancellationToken).AsTask();

    public void RemoveSignalOverride(MachineSignalOverride signalOverride) => dbContext.MachineSignalOverrides.Remove(signalOverride);

    public Task<List<ConfigurationVersion>> ListVersionsAsync(CancellationToken cancellationToken) =>
        dbContext.ConfigurationVersions.AsNoTracking().OrderByDescending(version => version.Version).ToListAsync(cancellationToken);

    public Task<ConfigurationVersion?> GetActiveVersionAsync(CancellationToken cancellationToken) =>
        dbContext.ConfigurationVersions.FirstOrDefaultAsync(version => version.Status == ConfigurationVersionStatus.Active, cancellationToken);

    public Task<ConfigurationVersion?> GetVersionAsync(long version, CancellationToken cancellationToken) =>
        dbContext.ConfigurationVersions.AsNoTracking().FirstOrDefaultAsync(item => item.Version == version, cancellationToken);

    public async Task<long> GetNextVersionNumberAsync(CancellationToken cancellationToken)
    {
        var latest = await dbContext.ConfigurationVersions.Select(version => (long?)version.Version).MaxAsync(cancellationToken);
        return (latest ?? 0) + 1;
    }

    public Task AddConfigurationVersionAsync(ConfigurationVersion version, CancellationToken cancellationToken) =>
        dbContext.ConfigurationVersions.AddAsync(version, cancellationToken).AsTask();

    public Task AddAuditLogAsync(AuditLog auditLog, CancellationToken cancellationToken) =>
        dbContext.AuditLogs.AddAsync(auditLog, cancellationToken).AsTask();

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
