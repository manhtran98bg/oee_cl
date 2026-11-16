using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.MesSync;

namespace Rostek.Gateway.Infrastructure.MesSync;

public sealed class HttpMesProductionOrderClient(
    HttpClient httpClient,
    IOptions<MesSyncOptions> options) : IMesProductionOrderClient
{
    private const string EndpointPath = "/api/v1/gateway/oee/production-orders";

    public async Task<MesProductionOrderPageDto> FetchPageAsync(
        int page,
        string productionStatus,
        CancellationToken cancellationToken)
    {
        var current = options.Value;
        if (string.IsNullOrWhiteSpace(current.BaseUrl))
        {
            throw new InvalidOperationException("MesSync:BaseUrl is empty.");
        }

        var endpoint = $"{current.BaseUrl.TrimEnd('/')}{EndpointPath}?page={page}&production_status={Uri.EscapeDataString(productionStatus)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(current.BearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", current.BearerToken);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, current.TimeoutSeconds)));
        using var response = await httpClient.SendAsync(request, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<MesProductionOrderPageDto>(cancellationToken: timeoutCts.Token)
            ?? throw new InvalidOperationException("MES production order response is empty.");
    }
}
