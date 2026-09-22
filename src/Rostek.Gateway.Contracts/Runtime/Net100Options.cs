namespace Rostek.Gateway.Contracts.Runtime;

public sealed class Net100Options
{
    public Dictionary<string, Net100CredentialOptions> Credentials { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Net100CredentialOptions? FindCredential(string reference) =>
        Credentials.FirstOrDefault(item => item.Key.Equals(reference, StringComparison.OrdinalIgnoreCase)).Value;
}

public sealed class Net100CredentialOptions
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
