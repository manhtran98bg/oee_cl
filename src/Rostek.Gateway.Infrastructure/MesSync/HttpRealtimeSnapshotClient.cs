using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.MesSync;

namespace Rostek.Gateway.Infrastructure.MesSync;

public sealed class HttpRealtimeSnapshotClient(
    HttpClient httpClient,
    IOptions<MesSyncOptions> options) : IRealtimeSnapshotClient
{
    private const string EndpointPath = "/api/v1/gateway/oee/realtime-snapshots";

    public async Task SendAsync(RealtimeSnapshotBatchPayload payload, CancellationToken cancellationToken)
    {
        var current = options.Value;
        if (string.IsNullOrWhiteSpace(current.BaseUrl))
        {
            throw new InvalidOperationException("MesSync:BaseUrl is empty.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(current.BaseUrl))
        {
            Content = JsonContent.Create(payload)
        };
        if (!string.IsNullOrWhiteSpace(current.BearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", current.BearerToken);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, current.TimeoutSeconds)));
        using var response = await httpClient.SendAsync(request, timeoutCts.Token);
        response.EnsureSuccessStatusCode();
    }

    private static string BuildEndpoint(string baseUrl) =>
        $"{baseUrl.TrimEnd('/')}{EndpointPath}";
}

public sealed class HttpSyncOutboxClient(
    HttpClient httpClient,
    IOptions<MesSyncOptions> options) : ISyncOutboxHttpClient
{
    public async Task SendAsync(string endpointPath, string payloadJson, CancellationToken cancellationToken)
    {
        var current = options.Value;
        if (string.IsNullOrWhiteSpace(current.BaseUrl))
        {
            throw new InvalidOperationException("MesSync:BaseUrl is empty.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(current.BaseUrl, endpointPath))
        {
            Content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(current.BearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", current.BearerToken);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, current.TimeoutSeconds)));
        using var response = await httpClient.SendAsync(request, timeoutCts.Token);
        response.EnsureSuccessStatusCode();
    }

    private static string BuildEndpoint(string baseUrl, string endpointPath) =>
        $"{baseUrl.TrimEnd('/')}/{endpointPath.TrimStart('/')}";
}
