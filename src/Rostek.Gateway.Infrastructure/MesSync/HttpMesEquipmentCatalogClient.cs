using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.MesSync;

namespace Rostek.Gateway.Infrastructure.MesSync;

public sealed class HttpMesEquipmentCatalogClient(
    HttpClient httpClient,
    IOptions<MesSyncOptions> options) : IMesEquipmentCatalogClient
{
    private const string EndpointPath = "/api/v1/equipment/molding-machines";

    public async Task<IReadOnlyList<MesMoldingMachineDto>> FetchMoldingMachinesAsync(CancellationToken cancellationToken)
    {
        var current = options.Value;
        if (string.IsNullOrWhiteSpace(current.BaseUrl))
        {
            throw new InvalidOperationException("MesSync:BaseUrl is empty.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildEndpoint(current.BaseUrl));
        if (!string.IsNullOrWhiteSpace(current.BearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", current.BearerToken);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, current.TimeoutSeconds)));
        using var response = await httpClient.SendAsync(request, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        var catalog = await response.Content.ReadFromJsonAsync<MesMoldingMachineCatalogResponse>(cancellationToken: timeoutCts.Token);
        return catalog?.Items ?? [];
    }

    private static string BuildEndpoint(string baseUrl) =>
        $"{baseUrl.TrimEnd('/')}{EndpointPath}";

    private sealed record MesMoldingMachineCatalogResponse(
        [property: JsonPropertyName("items")] IReadOnlyList<MesMoldingMachineDto>? Items);
}
