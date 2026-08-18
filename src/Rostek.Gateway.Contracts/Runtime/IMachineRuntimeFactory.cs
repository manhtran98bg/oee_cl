using Rostek.Gateway.Contracts.Configuration;

namespace Rostek.Gateway.Contracts.Runtime;

public interface IMachineRuntimeFactory
{
    IMachineRuntime Create(EffectiveMachineConfiguration configuration);
}
