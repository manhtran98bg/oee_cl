using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Contracts.Runtime;

namespace Rostek.Gateway.Runtime.Net100;

public interface INet100ClientAdapter : IAsyncDisposable
{
    Task<string> ReadLiveAsync(string machineAddress, int timeoutMs, CancellationToken cancellationToken);
}

public interface INet100ClientAdapterFactory
{
    INet100ClientAdapter Create(Uri baseAddress, string? authenticationMode, string? credentialReference);
}

public sealed class Net100ClientAdapterFactory(IOptions<Net100Options> options) : INet100ClientAdapterFactory
{
    public INet100ClientAdapter Create(Uri baseAddress, string? authenticationMode, string? credentialReference)
    {
        var mode = string.IsNullOrWhiteSpace(authenticationMode)
            ? "NONE"
            : authenticationMode.Trim().ToUpperInvariant();
        if (mode == "NONE")
        {
            return new Net100ClientAdapter(baseAddress);
        }

        if (mode != "BASIC")
        {
            throw new InvalidOperationException($"Unsupported NET100 authentication mode '{mode}'.");
        }

        if (string.IsNullOrWhiteSpace(credentialReference))
        {
            throw new InvalidOperationException("NET100 Basic authentication requires a credential reference.");
        }

        var credential = options.Value.FindCredential(credentialReference.Trim());
        if (credential is null || string.IsNullOrWhiteSpace(credential.Username))
        {
            throw new InvalidOperationException($"NET100 credential reference '{credentialReference.Trim()}' is not configured.");
        }

        return new Net100ClientAdapter(baseAddress, credential.Username, credential.Password);
    }
}

public sealed class Net100ClientAdapter : INet100ClientAdapter
{
    private readonly HttpClient _httpClient;

    public Net100ClientAdapter(
        Uri baseAddress,
        string? username = null,
        string? password = null,
        HttpMessageHandler? handler = null)
    {
        var normalized = new Uri(baseAddress.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _httpClient.BaseAddress = normalized;
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        if (!string.IsNullOrWhiteSpace(username))
        {
            var encodedCredential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password ?? string.Empty}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encodedCredential);
        }
    }

    public async Task<string> ReadLiveAsync(string machineAddress, int timeoutMs, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(Math.Max(1, timeoutMs));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var relativePath = $"machine/{Uri.EscapeDataString(machineAddress.Trim())}/live";
            using var response = await _httpClient.GetAsync(relativePath, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(linked.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"NET100 live request timed out after {timeoutMs} ms.", exception);
        }
    }

    public ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
