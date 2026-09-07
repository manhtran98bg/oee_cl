using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.MesSync;

namespace Rostek.Gateway.Infrastructure.MesSync;

public sealed class MesServerClient(
    IHttpClientFactory httpClientFactory,
    IOptions<MesSyncOptions> options,
    ILogger<MesServerClient> logger) : IMesServerClient
{
    public async Task SendAsync(string endpoint, string payloadJson, CancellationToken cancellationToken)
    {
        var current = options.Value;
        if (string.IsNullOrWhiteSpace(current.BaseUrl))
        {
            throw new InvalidOperationException("MesSync:BaseUrl is required when MES sync is enabled.");
        }

        var attempts = Math.Max(1, current.RetryCount + 1);
        Exception? lastError = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using var request = CreateRequest(current, endpoint, payloadJson);
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, current.TimeoutSeconds)));

                var client = httpClientFactory.CreateClient(nameof(MesServerClient));
                using var response = await client.SendAsync(request, timeoutCts.Token);
                var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                EnsureSuccess(response.StatusCode, responseBody);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                logger.LogWarning(ex, "MES HTTP request failed. Endpoint={Endpoint}, Attempt={Attempt}, MaxAttempts={MaxAttempts}", endpoint, attempt, attempts);
                if (attempt < attempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }
        }

        throw new InvalidOperationException($"MES HTTP request failed after {attempts} attempts.", lastError);
    }

    private static HttpRequestMessage CreateRequest(MesSyncOptions options, string endpoint, string payloadJson)
    {
        var baseUri = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, endpoint.TrimStart('/')))
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(options.BearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.BearerToken.Trim());
        }

        return request;
    }

    private static void EnsureSuccess(HttpStatusCode statusCode, string responseBody)
    {
        if (statusCode is not (HttpStatusCode.OK or HttpStatusCode.Created))
        {
            throw new HttpRequestException($"MES server returned HTTP {(int)statusCode}.");
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return;
        }

        using var document = JsonDocument.Parse(responseBody);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("msg", out var msg) ||
            msg.ValueKind != JsonValueKind.String ||
            msg.GetString()?.Equals("OK", StringComparison.OrdinalIgnoreCase) == true ||
            document.RootElement.TryGetProperty("metaData", out _))
        {
            return;
        }

        throw new HttpRequestException($"MES server returned message '{msg.GetString()}'.");
    }
}
