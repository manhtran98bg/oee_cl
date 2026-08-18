using Rostek.Gateway.Contracts.Configuration;

namespace Rostek.Gateway.Contracts.Runtime;

public interface IRuntimeConfigurationProvider
{
    RuntimeConfiguration Current { get; }

    Task ReplaceAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken);
}
