namespace Rostek.Gateway.Runtime.Net100;

public interface INet100ClientAdapter : IAsyncDisposable
{
    Task<string> ReadLiveAsync(string machineAddress, int timeoutMs, CancellationToken cancellationToken);
}

public interface INet100ClientAdapterFactory
{
    INet100ClientAdapter Create(Uri baseAddress);
}

public sealed class Net100ClientAdapterFactory : INet100ClientAdapterFactory
{
    public INet100ClientAdapter Create(Uri baseAddress) => new Net100ClientAdapter(baseAddress);
}

public sealed class Net100ClientAdapter : INet100ClientAdapter
{
    private readonly HttpClient _httpClient;

    public Net100ClientAdapter(Uri baseAddress, HttpMessageHandler? handler = null)
    {
        var normalized = new Uri(baseAddress.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _httpClient.BaseAddress = normalized;
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
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
