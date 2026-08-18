using Microsoft.Extensions.Logging;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.Runtime;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Domain.Enums;

namespace Rostek.Gateway.Application.Configurations;

public sealed class ConfigurationApplyService(
    IConfigRepository repository,
    IConfigurationBuilder builder,
    IConfigurationValidator validator,
    IRuntimeConfigurationProvider runtimeProvider,
    IMachineRuntimeManager runtimeManager,
    ILogger<ConfigurationApplyService> logger) : IConfigurationApplyService
{
    public async Task<ConfigurationApplyResult> ApplyDraftAsync(string? description, string? userName, CancellationToken cancellationToken)
    {
        var draft = await builder.BuildDraftAsync(cancellationToken);
        logger.LogInformation("Applying draft configuration with {MachineCount} machines", draft.Machines.Count);
        var validation = await validator.ValidateAsync(draft, cancellationToken);
        if (!validation.IsValid)
        {
            logger.LogWarning("Draft configuration apply blocked by {IssueCount} validation issues", validation.Issues.Count);
            return new ConfigurationApplyResult(false, null, validation, "Configuration has validation errors.");
        }

        var nextVersion = await repository.GetNextVersionNumberAsync(cancellationToken);
        var appliedAtUtc = DateTimeOffset.UtcNow;
        var current = draft with { Version = nextVersion, AppliedAtUtc = appliedAtUtc };
        return await PersistAndApplyAsync(current, description, userName, validation, cancellationToken);
    }

    public async Task<ConfigurationApplyResult> RollbackAsync(long sourceVersion, string? description, string? userName, CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting configuration rollback from version {SourceVersion}", sourceVersion);
        var source = await builder.BuildVersionAsync(sourceVersion, cancellationToken);
        var nextVersion = await repository.GetNextVersionNumberAsync(cancellationToken);
        var rollback = source with { Version = nextVersion, AppliedAtUtc = DateTimeOffset.UtcNow };
        var validation = await validator.ValidateAsync(rollback, cancellationToken);
        if (!validation.IsValid)
        {
            logger.LogWarning("Configuration rollback from version {SourceVersion} blocked by {IssueCount} validation issues", sourceVersion, validation.Issues.Count);
            return new ConfigurationApplyResult(false, null, validation, "Rollback source has validation errors.");
        }

        return await PersistAndApplyAsync(rollback, description ?? $"Rollback from version {sourceVersion}", userName, validation, cancellationToken);
    }

    private async Task<ConfigurationApplyResult> PersistAndApplyAsync(RuntimeConfiguration current, string? description, string? userName, ConfigurationValidationResult validation, CancellationToken cancellationToken)
    {
        var previous = runtimeProvider.Current;
        var json = ConfigurationJson.Serialize(current);
        if (!ConfigurationJson.LooksSecretFree(json))
        {
            logger.LogWarning("Configuration version {Version} was blocked because snapshot contains secret-looking fields", current.Version);
            return new ConfigurationApplyResult(false, null, validation, "Snapshot contains secret-looking fields.");
        }

        var checksum = ConfigurationJson.Checksum(json);
        logger.LogInformation("Persisting configuration version {Version} with checksum {Checksum}", current.Version, checksum);
        var versionEntity = new ConfigurationVersion
        {
            Version = current.Version,
            Status = ConfigurationVersionStatus.Active,
            SnapshotJson = json,
            Checksum = checksum,
            Description = description,
            CreatedBy = userName,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            AppliedAtUtc = current.AppliedAtUtc
        };

        var active = await repository.GetActiveVersionAsync(cancellationToken);
        if (active is not null)
        {
            active.Status = ConfigurationVersionStatus.Superseded;
        }

        await repository.AddConfigurationVersionAsync(versionEntity, cancellationToken);
        await repository.AddAuditLogAsync(new AuditLog
        {
            UserName = userName,
            Action = "Apply configuration",
            EntityType = nameof(ConfigurationVersion),
            EntityId = current.Version.ToString(),
            NewValueJson = $$"""{"version":{{current.Version}},"checksum":"{{checksum}}"}""",
            CreatedAtUtc = DateTimeOffset.UtcNow
        }, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        try
        {
            await runtimeProvider.ReplaceAsync(current, cancellationToken);
            await runtimeManager.ApplyConfigurationAsync(previous, current, cancellationToken);
            logger.LogInformation("Applied configuration version {Version} to runtime. MachineCount={MachineCount}", current.Version, current.Machines.Count);
            return new ConfigurationApplyResult(true, current.Version, validation, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply configuration version {Version} to runtime", current.Version);
            versionEntity.Status = ConfigurationVersionStatus.Failed;
            if (active is not null)
            {
                active.Status = ConfigurationVersionStatus.Active;
            }

            await runtimeProvider.ReplaceAsync(previous, CancellationToken.None);
            await repository.AddAuditLogAsync(new AuditLog
            {
                UserName = userName,
                Action = "Apply configuration failed",
                EntityType = nameof(ConfigurationVersion),
                EntityId = current.Version.ToString(),
                NewValueJson = $$"""{"error":"{{ex.Message}}"}""",
                CreatedAtUtc = DateTimeOffset.UtcNow
            }, CancellationToken.None);
            await repository.SaveChangesAsync(CancellationToken.None);
            return new ConfigurationApplyResult(false, current.Version, validation, ex.Message);
        }
    }
}
