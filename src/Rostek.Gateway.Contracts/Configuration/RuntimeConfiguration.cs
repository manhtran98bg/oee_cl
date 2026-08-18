namespace Rostek.Gateway.Contracts.Configuration;

public sealed record RuntimeConfiguration(
    long Version,
    DateTimeOffset AppliedAtUtc,
    IReadOnlyDictionary<string, EffectiveMachineConfiguration> Machines)
{
    public static RuntimeConfiguration Empty { get; } =
        new(0, DateTimeOffset.UnixEpoch, new Dictionary<string, EffectiveMachineConfiguration>(StringComparer.OrdinalIgnoreCase));
}
