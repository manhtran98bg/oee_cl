using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.Machines;
using Rostek.Gateway.Contracts.Runtime;

namespace Rostek.Gateway.Runtime.Machines;

public sealed class FakeMachineRuntime(EffectiveMachineConfiguration configuration) : IMachineRuntime
{
    private EffectiveMachineConfiguration _configuration = configuration;
    private MachineRuntimeStatusDto _status = new(configuration.MachineCode, MachineRuntimeState.Stopped, "Created", DateTimeOffset.UtcNow, 0, 0);

    public string MachineCode => _configuration.MachineCode;
    public MachineRuntimeStatusDto Status => _status;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_configuration.Enabled)
        {
            _status = _status with { State = MachineRuntimeState.Disabled, Message = "Machine disabled", UpdatedAtUtc = DateTimeOffset.UtcNow };
            return Task.CompletedTask;
        }

        _status = _status with
        {
            State = MachineRuntimeState.Connected,
            Message = "Fake runtime connected",
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            StartCount = _status.StartCount + 1
        };
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _status = _status with
        {
            State = MachineRuntimeState.Stopped,
            Message = "Fake runtime stopped",
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            StopCount = _status.StopCount + 1
        };
        return Task.CompletedTask;
    }

    public Task ApplyConfigurationAsync(EffectiveMachineConfiguration configuration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _configuration = configuration;
        _status = _status with { Message = "Fake configuration applied", UpdatedAtUtc = DateTimeOffset.UtcNow };
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
    }
}

public sealed class FakeMachineRuntimeFactory : IMachineRuntimeFactory
{
    public IMachineRuntime Create(EffectiveMachineConfiguration configuration) => new FakeMachineRuntime(configuration);
}
