using Microsoft.Extensions.Logging;
using Rostek.Gateway.Application.Common;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Machines;

public sealed class SignalOverrideService(IConfigRepository repository, ILogger<SignalOverrideService> logger) : ISignalOverrideService
{
    public async Task<GatewayResult> SaveAsync(SignalOverrideInput input, string? userName, CancellationToken cancellationToken)
    {
        var machine = await repository.GetMachineAsync(input.MachineId, includeDetails: true, cancellationToken);
        if (machine is null)
        {
            return GatewayResult.Fail("Machine not found.");
        }

        var templateSignal = machine.Template?.Signals.SingleOrDefault(signal => signal.Id == input.TemplateSignalId);
        if (templateSignal is null)
        {
            return GatewayResult.Fail("Template signal not found for this machine.");
        }

        if (templateSignal.Required && input.Enabled == false)
        {
            return GatewayResult.Fail("Required signal cannot be disabled.");
        }

        var existing = await repository.GetSignalOverrideAsync(input.MachineId, input.TemplateSignalId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var signalOverride = existing ?? new MachineSignalOverride
        {
            MachineId = input.MachineId,
            TemplateSignalId = input.TemplateSignalId,
            CreatedAtUtc = now
        };

        signalOverride.SourceAddress = NullIfWhiteSpace(input.SourceAddress);
        signalOverride.DataType = input.DataType;
        signalOverride.SamplingIntervalMs = input.SamplingIntervalMs;
        signalOverride.ScalingFactor = input.ScalingFactor;
        signalOverride.ScalingOffset = input.ScalingOffset;
        signalOverride.Enabled = input.Enabled;
        signalOverride.ValueMappingJson = NullIfWhiteSpace(input.ValueMappingJson);
        signalOverride.OptionsJson = NullIfWhiteSpace(input.OptionsJson);
        signalOverride.UpdatedAtUtc = now;

        if (existing is null)
        {
            await repository.AddSignalOverrideAsync(signalOverride, cancellationToken);
        }

        await repository.AddAuditLogAsync(new AuditLog
        {
            UserName = userName,
            Action = existing is null ? "Create signal override" : "Update signal override",
            EntityType = nameof(MachineSignalOverride),
            EntityId = signalOverride.Id.ToString(),
            CreatedAtUtc = now
        }, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Saved signal override {OverrideId} for machine {MachineCode}, signal {SignalCode}. Action={Action}",
            signalOverride.Id,
            machine.Code,
            templateSignal.SignalCode,
            existing is null ? "Create" : "Update");
        return GatewayResult.Ok();
    }

    public async Task<GatewayResult> ResetAsync(Guid machineId, Guid templateSignalId, string? userName, CancellationToken cancellationToken)
    {
        var existing = await repository.GetSignalOverrideAsync(machineId, templateSignalId, cancellationToken);
        if (existing is null)
        {
            return GatewayResult.Ok();
        }

        repository.RemoveSignalOverride(existing);
        await repository.AddAuditLogAsync(new AuditLog
        {
            UserName = userName,
            Action = "Reset signal override",
            EntityType = nameof(MachineSignalOverride),
            EntityId = existing.Id.ToString(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        }, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Reset signal override {OverrideId} for machine {MachineId}, template signal {TemplateSignalId}", existing.Id, machineId, templateSignalId);
        return GatewayResult.Ok();
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
