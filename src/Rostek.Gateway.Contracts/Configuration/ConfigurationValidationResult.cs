namespace Rostek.Gateway.Contracts.Configuration;

public sealed record ConfigurationValidationResult(
    bool IsValid,
    IReadOnlyList<ConfigurationValidationIssue> Issues)
{
    public static ConfigurationValidationResult Success { get; } = new(true, []);
}

public sealed record ConfigurationValidationIssue(
    string Severity,
    string Code,
    string Message,
    string? MachineCode,
    string? SignalCode,
    string? Field);
