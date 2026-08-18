using Microsoft.Extensions.Logging;
using Rostek.Gateway.Application.Common;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.MachineGroups;

public sealed class MachineGroupService(IConfigRepository repository, ILogger<MachineGroupService> logger) : IMachineGroupService
{
    public async Task<IReadOnlyList<MachineGroupListItem>> ListAsync(CancellationToken cancellationToken)
    {
        var groups = await repository.ListGroupsAsync(cancellationToken);
        return groups
            .OrderBy(group => group.DisplayOrder)
            .ThenBy(group => group.Code)
            .Select(group => new MachineGroupListItem(group.Id, group.Code, group.Name, group.Description, group.DisplayOrder))
            .ToList();
    }

    public async Task<MachineGroupInput?> GetInputAsync(Guid id, CancellationToken cancellationToken)
    {
        var group = await repository.GetGroupAsync(id, cancellationToken);
        return group is null
            ? null
            : new MachineGroupInput
            {
                Id = group.Id,
                Code = group.Code,
                Name = group.Name,
                Description = group.Description,
                DisplayOrder = group.DisplayOrder
            };
    }

    public async Task<GatewayResult<Guid>> SaveAsync(MachineGroupInput input, string? userName, CancellationToken cancellationToken)
    {
        var code = NormalizeCode(input.Code);
        if (string.IsNullOrWhiteSpace(code))
        {
            return GatewayResult<Guid>.Fail("Group code is required.");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return GatewayResult<Guid>.Fail("Group name is required.");
        }

        if (await repository.GroupCodeExistsAsync(code, input.Id, cancellationToken))
        {
            return GatewayResult<Guid>.Fail($"Group code '{code}' already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        MachineGroup group;
        if (input.Id is Guid id)
        {
            group = await repository.GetGroupAsync(id, cancellationToken) ?? throw new InvalidOperationException("Group not found.");
            group.UpdatedAtUtc = now;
        }
        else
        {
            group = new MachineGroup { CreatedAtUtc = now, UpdatedAtUtc = now };
            await repository.AddGroupAsync(group, cancellationToken);
        }

        group.Code = code;
        group.Name = input.Name.Trim();
        group.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        group.DisplayOrder = input.DisplayOrder;

        await repository.AddAuditLogAsync(new AuditLog
        {
            UserName = userName,
            Action = input.Id is null ? "Create machine group" : "Update machine group",
            EntityType = nameof(MachineGroup),
            EntityId = group.Id.ToString(),
            CreatedAtUtc = now
        }, cancellationToken);

        await repository.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Saved machine group {GroupCode} ({GroupId}). Action={Action}", group.Code, group.Id, input.Id is null ? "Create" : "Update");
        return GatewayResult<Guid>.Ok(group.Id);
    }

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
}
