using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.Runtime;

namespace Rostek.Gateway.Runtime.Configuration;

public sealed class RuntimeConfigurationProvider : IRuntimeConfigurationProvider
{
    private RuntimeConfiguration _current = RuntimeConfiguration.Empty;

    public RuntimeConfiguration Current => Volatile.Read(ref _current);

    public Task ReplaceAsync(RuntimeConfiguration configuration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref _current, configuration);
        return Task.CompletedTask;
    }
}
