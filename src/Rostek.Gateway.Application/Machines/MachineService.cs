using Microsoft.Extensions.Logging;
using Rostek.Gateway.Application.Common;
using Rostek.Gateway.Application.Configurations;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Domain.Enums;

namespace Rostek.Gateway.Application.Machines;

public sealed class MachineService(
    IConfigRepository repository,
    IConfigurationBuilder configurationBuilder,
    ILogger<MachineService> logger) : IMachineService
{
    public async Task<MachineListResult> ListAsync(MachineQuery query, CancellationToken cancellationToken)
    {
        var machines = await repository.ListMachinesAsync(query, cancellationToken);
        var items = machines.Select(machine => new MachineListItem(
            machine.Id,
            machine.Code,
            machine.Name,
            machine.Group?.Code,
            machine.Template?.Code ?? string.Empty,
            machine.Template?.Protocol ?? machine.Connection?.Protocol ?? GatewayProtocol.OpcUa,
            FormatEndpoint(machine.Connection),
            machine.Enabled)).ToList();

        return new MachineListResult(items, Math.Max(1, query.Page), Math.Max(1, query.PageSize));
    }

    public async Task<MachineEditInput?> GetInputAsync(Guid id, CancellationToken cancellationToken)
    {
        var machine = await repository.GetMachineAsync(id, includeDetails: true, cancellationToken);
        if (machine is null)
        {
            return null;
        }

        var input = new MachineEditInput
        {
            Id = machine.Id,
            Code = machine.Code,
            Name = machine.Name,
            GroupId = machine.GroupId,
            TemplateId = machine.TemplateId,
            Enabled = machine.Enabled,
            DisplayOrder = machine.DisplayOrder,
            Description = machine.Description
        };

        if (machine.Connection?.Protocol == GatewayProtocol.OpcUa)
        {
            input.OpcUa.EndpointUrl = machine.Connection.EndpointUrl;
            input.OpcUa.SecurityMode = machine.Connection.SecurityMode;
            input.OpcUa.SecurityPolicy = machine.Connection.SecurityPolicy;
            input.OpcUa.AuthenticationMode = machine.Connection.AuthenticationMode;
            input.OpcUa.CredentialReference = machine.Connection.CredentialReference;
            input.OpcUa.ConnectTimeoutMs = machine.Connection.ConnectTimeoutMs;
            input.OpcUa.RequestTimeoutMs = machine.Connection.RequestTimeoutMs;
        }
        else if (machine.Connection?.Protocol == GatewayProtocol.ModbusTcp)
        {
            input.ModbusTcp.Host = machine.Connection.Host;
            input.ModbusTcp.Port = machine.Connection.Port;
            input.ModbusTcp.UnitId = machine.Connection.UnitId;
            input.ModbusTcp.ConnectTimeoutMs = machine.Connection.ConnectTimeoutMs;
            input.ModbusTcp.RequestTimeoutMs = machine.Connection.RequestTimeoutMs;
            input.ModbusTcp.RetryCount = machine.Connection.RetryCount;
            input.ModbusTcp.PollingIntervalMs = machine.Connection.PollingIntervalMs;
            input.ModbusTcp.OptionsJson = machine.Connection.OptionsJson;
        }

        return input;
    }

    public async Task<GatewayResult<Guid>> SaveAsync(MachineEditInput input, string? userName, CancellationToken cancellationToken)
    {
        var template = await repository.GetTemplateAsync(input.TemplateId, includeSignals: false, cancellationToken);
        if (template is null)
        {
            return GatewayResult<Guid>.Fail("Template is required.");
        }

        var code = NormalizeCode(input.Code);
        if (string.IsNullOrWhiteSpace(code))
        {
            return GatewayResult<Guid>.Fail("Machine code is required.");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return GatewayResult<Guid>.Fail("Machine name is required.");
        }

        if (await repository.MachineCodeExistsAsync(code, input.Id, cancellationToken))
        {
            return GatewayResult<Guid>.Fail($"Machine code '{code}' already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        Machine machine;
        if (input.Id is Guid id)
        {
            machine = await repository.GetMachineAsync(id, includeDetails: true, cancellationToken) ?? throw new InvalidOperationException("Machine not found.");
            machine.UpdatedAtUtc = now;
        }
        else
        {
            machine = new Machine { CreatedAtUtc = now, UpdatedAtUtc = now };
            await repository.AddMachineAsync(machine, cancellationToken);
        }

        machine.Code = code;
        machine.Name = input.Name.Trim();
        machine.GroupId = input.GroupId;
        machine.TemplateId = input.TemplateId;
        machine.Enabled = input.Enabled;
        machine.DisplayOrder = input.DisplayOrder;
        machine.Description = NullIfWhiteSpace(input.Description);

        var connection = machine.Connection ?? new MachineConnection { Machine = machine, CreatedAtUtc = now };
        connection.UpdatedAtUtc = now;
        ApplyConnectionInput(connection, template.Protocol, input);
        machine.Connection = connection;
        if (connection.Id == Guid.Empty)
        {
            connection.Id = Guid.NewGuid();
        }

        if (connection.MachineId == Guid.Empty && input.Id is not null)
        {
            connection.MachineId = machine.Id;
        }

        if (connection.MachineId == Guid.Empty)
        {
            await repository.AddConnectionAsync(connection, cancellationToken);
        }

        await repository.AddAuditLogAsync(new AuditLog
        {
            UserName = userName,
            Action = input.Id is null ? "Create machine" : "Update machine",
            EntityType = nameof(Machine),
            EntityId = machine.Id.ToString(),
            CreatedAtUtc = now
        }, cancellationToken);

        await repository.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Saved machine {MachineCode} ({MachineId}). Protocol={Protocol}, Enabled={Enabled}, Action={Action}",
            machine.Code,
            machine.Id,
            connection.Protocol,
            machine.Enabled,
            input.Id is null ? "Create" : "Update");
        return GatewayResult<Guid>.Ok(machine.Id);
    }

    public async Task<GatewayResult<Guid>> CloneAsync(Guid id, string newCode, string? userName, CancellationToken cancellationToken)
    {
        var source = await repository.GetMachineAsync(id, includeDetails: true, cancellationToken);
        if (source is null)
        {
            return GatewayResult<Guid>.Fail("Machine not found.");
        }

        var input = await GetInputAsync(id, cancellationToken);
        if (input is null)
        {
            return GatewayResult<Guid>.Fail("Machine not found.");
        }

        input.Id = null;
        input.Code = newCode;
        input.Name = $"{source.Name} Copy";
        return await SaveAsync(input, userName, cancellationToken);
    }

    public async Task<GatewayResult> SetEnabledAsync(Guid id, bool enabled, string? userName, CancellationToken cancellationToken)
    {
        var machine = await repository.GetMachineAsync(id, includeDetails: false, cancellationToken);
        if (machine is null)
        {
            return GatewayResult.Fail("Machine not found.");
        }

        machine.Enabled = enabled;
        machine.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await repository.AddAuditLogAsync(new AuditLog
        {
            UserName = userName,
            Action = enabled ? "Enable machine" : "Disable machine",
            EntityType = nameof(Machine),
            EntityId = machine.Id.ToString(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        }, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Set machine {MachineCode} ({MachineId}) enabled state to {Enabled}", machine.Code, machine.Id, enabled);
        return GatewayResult.Ok();
    }

    public async Task<RuntimeConfiguration?> PreviewEffectiveAsync(Guid machineId, CancellationToken cancellationToken)
    {
        var machine = await repository.GetMachineAsync(machineId, includeDetails: false, cancellationToken);
        if (machine is null)
        {
            return null;
        }

        var draft = await configurationBuilder.BuildDraftAsync(cancellationToken);
        return draft.Machines.ContainsKey(machine.Code)
            ? draft with { Machines = new Dictionary<string, EffectiveMachineConfiguration>(StringComparer.OrdinalIgnoreCase) { [machine.Code] = draft.Machines[machine.Code] } }
            : RuntimeConfiguration.Empty;
    }

    private static void ApplyConnectionInput(MachineConnection connection, GatewayProtocol protocol, MachineEditInput input)
    {
        connection.Protocol = protocol;
        if (protocol == GatewayProtocol.OpcUa)
        {
            connection.EndpointUrl = NullIfWhiteSpace(input.OpcUa.EndpointUrl);
            connection.SecurityMode = NullIfWhiteSpace(input.OpcUa.SecurityMode) ?? "NONE";
            connection.SecurityPolicy = NullIfWhiteSpace(input.OpcUa.SecurityPolicy) ?? "None";
            connection.AuthenticationMode = NullIfWhiteSpace(input.OpcUa.AuthenticationMode) ?? "ANONYMOUS";
            connection.CredentialReference = NullIfWhiteSpace(input.OpcUa.CredentialReference);
            connection.ConnectTimeoutMs = input.OpcUa.ConnectTimeoutMs;
            connection.RequestTimeoutMs = input.OpcUa.RequestTimeoutMs;
            connection.Host = null;
            connection.Port = null;
            connection.UnitId = null;
            connection.OptionsJson = null;
        }
        else
        {
            connection.Host = NullIfWhiteSpace(input.ModbusTcp.Host);
            connection.Port = input.ModbusTcp.Port ?? 502;
            connection.UnitId = input.ModbusTcp.UnitId;
            connection.ConnectTimeoutMs = input.ModbusTcp.ConnectTimeoutMs;
            connection.RequestTimeoutMs = input.ModbusTcp.RequestTimeoutMs;
            connection.RetryCount = input.ModbusTcp.RetryCount;
            connection.PollingIntervalMs = input.ModbusTcp.PollingIntervalMs;
            connection.OptionsJson = NullIfWhiteSpace(input.ModbusTcp.OptionsJson);
            connection.EndpointUrl = null;
            connection.SecurityMode = null;
            connection.SecurityPolicy = null;
            connection.AuthenticationMode = null;
            connection.CredentialReference = null;
        }
    }

    private static string FormatEndpoint(MachineConnection? connection) =>
        connection?.Protocol == GatewayProtocol.OpcUa
            ? connection.EndpointUrl ?? string.Empty
            : $"{connection?.Host}:{connection?.Port}";

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
