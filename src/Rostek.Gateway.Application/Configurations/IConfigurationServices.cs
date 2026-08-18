using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.ImportExport;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Configurations;

public interface IConfigurationBuilder
{
    Task<RuntimeConfiguration> BuildDraftAsync(CancellationToken cancellationToken);
    Task<RuntimeConfiguration> BuildVersionAsync(long version, CancellationToken cancellationToken);
}

public interface IConfigurationValidator
{
    Task<ConfigurationValidationResult> ValidateAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken);
}

public interface IConfigurationApplyService
{
    Task<ConfigurationApplyResult> ApplyDraftAsync(string? description, string? userName, CancellationToken cancellationToken);
    Task<ConfigurationApplyResult> RollbackAsync(long sourceVersion, string? description, string? userName, CancellationToken cancellationToken);
}

public interface IConfigurationVersionService
{
    Task<IReadOnlyList<ConfigurationVersion>> ListAsync(CancellationToken cancellationToken);
    Task<RuntimeConfiguration?> GetActiveAsync(CancellationToken cancellationToken);
    Task<RuntimeConfiguration?> GetVersionAsync(long version, CancellationToken cancellationToken);
}

public interface IConfigurationImportExportService
{
    Task<string> ExportActiveJsonAsync(CancellationToken cancellationToken);
    Task<string> ExportDraftJsonAsync(CancellationToken cancellationToken);
    Task<ImportPreview> PreviewJsonAsync(string json, CancellationToken cancellationToken);
    Task<ImportPreview> PreviewMachinesCsvAsync(string csv, CancellationToken cancellationToken);
    Task<ImportPreview> CommitMachinesCsvAsync(string csv, bool createMissingGroups, bool updateExisting, string? userName, CancellationToken cancellationToken);
}
