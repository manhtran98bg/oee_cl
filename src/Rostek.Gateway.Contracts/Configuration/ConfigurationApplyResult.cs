namespace Rostek.Gateway.Contracts.Configuration;

public sealed record ConfigurationApplyResult(
    bool Succeeded,
    long? Version,
    ConfigurationValidationResult Validation,
    string? ErrorMessage);
