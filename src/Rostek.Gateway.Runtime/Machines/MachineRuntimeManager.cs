using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.Machines;
using Rostek.Gateway.Contracts.Runtime;
using Rostek.Gateway.Runtime.Configuration;
using Rostek.Gateway.Runtime.Net100;

namespace Rostek.Gateway.Runtime.Machines;

public sealed class MachineRuntimeManager(
    IMachineRuntimeFactory factory,
    ConfigurationDiffService diffService,
    IOptions<RuntimeOptions> options,
    MachineValueStore valueStore,
    Net100RuntimeCoordinator net100Coordinator,
    ILogger<MachineRuntimeManager> logger) : IMachineRuntimeManager, IRuntimeStatusReader
{
    private readonly Dictionary<string, IMachineRuntime> _runtimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _initialConnectLimiter = new(Math.Max(1, options.Value.MaxInitialConcurrentConnections));

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task ApplyConfigurationAsync(RuntimeConfiguration previous, RuntimeConfiguration current, CancellationToken cancellationToken)
    {
        await net100Coordinator.ApplyConfigurationAsync(current, cancellationToken);

        foreach (var change in diffService.Diff(previous, current).Where(change => change.ChangeType != MachineConfigurationChangeType.Unchanged))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (Net100Configuration.IsProtocol(change.Current?.Protocol))
                {
                    if (change.Previous is not null && !Net100Configuration.IsProtocol(change.Previous.Protocol))
                    {
                        await StopAndRemoveAsync(change.MachineCode, cancellationToken);
                    }

                    continue;
                }

                if (change.Current is null && Net100Configuration.IsProtocol(change.Previous?.Protocol))
                {
                    continue;
                }

                await ApplyChangeAsync(change, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to apply runtime change {ChangeType} for machine {MachineCode}", change.ChangeType, change.MachineCode);
            }
        }
    }

    public IReadOnlyCollection<MachineRuntimeStatusDto> GetStatuses() =>
        _runtimes.Values.Select(runtime => runtime.Status)
            .Concat(net100Coordinator.GetStatuses())
            .OrderBy(status => status.MachineCode)
            .ToList();

    public MachineRuntimeStatusDto? GetStatus(string machineCode) =>
        _runtimes.TryGetValue(machineCode, out var runtime) ? runtime.Status : net100Coordinator.GetStatus(machineCode);

    private async Task ApplyChangeAsync(MachineConfigurationChange change, CancellationToken cancellationToken)
    {
        switch (change.ChangeType)
        {
            case MachineConfigurationChangeType.Added:
            case MachineConfigurationChangeType.Enabled:
                await StartOrReplaceAsync(change.Current!, cancellationToken);
                break;
            case MachineConfigurationChangeType.Removed:
            case MachineConfigurationChangeType.Disabled:
                await StopAndRemoveAsync(change.MachineCode, cancellationToken);
                break;
            case MachineConfigurationChangeType.ConnectionChanged:
            case MachineConfigurationChangeType.TemplateChanged:
                await StopAndRemoveAsync(change.MachineCode, cancellationToken);
                await StartOrReplaceAsync(change.Current!, cancellationToken);
                break;
            case MachineConfigurationChangeType.SignalsChanged:
                if (_runtimes.TryGetValue(change.MachineCode, out var runtime))
                {
                    await runtime.ApplyConfigurationAsync(change.Current!, cancellationToken);
                }
                else
                {
                    await StartOrReplaceAsync(change.Current!, cancellationToken);
                }
                break;
            case MachineConfigurationChangeType.Unchanged:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }
    }

    private async Task StartOrReplaceAsync(EffectiveMachineConfiguration configuration, CancellationToken cancellationToken)
    {
        if (_runtimes.ContainsKey(configuration.MachineCode))
        {
            await StopAndRemoveAsync(configuration.MachineCode, cancellationToken);
        }

        var runtime = factory.Create(configuration);
        _runtimes[configuration.MachineCode] = runtime;
        await _initialConnectLimiter.WaitAsync(cancellationToken);
        try
        {
            await runtime.StartAsync(cancellationToken);
        }
        finally
        {
            _initialConnectLimiter.Release();
        }
    }

    private async Task StopAndRemoveAsync(string machineCode, CancellationToken cancellationToken)
    {
        if (!_runtimes.Remove(machineCode, out var runtime))
        {
            return;
        }

        await runtime.StopAsync(cancellationToken);
        await runtime.DisposeAsync();
        valueStore.Remove(machineCode);
    }
}
