using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Configurations;

public sealed class ConfigurationVersionService(IConfigRepository repository) : IConfigurationVersionService
{
    public async Task<IReadOnlyList<ConfigurationVersion>> ListAsync(CancellationToken cancellationToken) =>
        (await repository.ListVersionsAsync(cancellationToken)).OrderByDescending(version => version.Version).ToList();

    public async Task<RuntimeConfiguration?> GetActiveAsync(CancellationToken cancellationToken)
    {
        var active = await repository.GetActiveVersionAsync(cancellationToken);
        return active is null ? null : ConfigurationJson.Deserialize(active.SnapshotJson);
    }

    public async Task<RuntimeConfiguration?> GetVersionAsync(long version, CancellationToken cancellationToken)
    {
        var entity = await repository.GetVersionAsync(version, cancellationToken);
        return entity is null ? null : ConfigurationJson.Deserialize(entity.SnapshotJson);
    }
}
