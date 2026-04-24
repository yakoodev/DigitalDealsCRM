using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDCRM.Shared.Constants;
using DDCRM.Shared.Errors;
using Microsoft.Extensions.Options;

namespace DDCRM.Gateway.Api.Clients;

public sealed class WorkerProxyHttpClient(
    HttpClient httpClient,
    IOptions<WorkerProxyClientOptions> options)
    : IWorkerProxyClient
{
    private readonly WorkerProxyClientOptions _options = options.Value;

    public async Task<bool> SupportsActionAsync(RouteResolution route, string action, CancellationToken cancellationToken)
    {
        EnsureEnabled();

        if (!action.StartsWith("ext.", StringComparison.Ordinal))
        {
            return true;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildAbsoluteUri(route, BuildPath("/capabilities")));
        ApplyServiceHeaders(request, idempotencyKey: null);

        var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                "Worker capabilities недоступны.",
                new Dictionary<string, object?>
                {
                    ["statusCode"] = (int)response.StatusCode,
                    ["body"] = payload,
                });
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("capabilities", out var capabilitiesElement)
            || capabilitiesElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var capability in capabilitiesElement.EnumerateArray())
        {
            var key = capability.TryGetProperty("key", out var keyElement) ? keyElement.GetString() : null;
            var enabled = capability.TryGetProperty("enabled", out var enabledElement)
                && enabledElement.ValueKind == JsonValueKind.True;

            if (enabled && string.Equals(key, action, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<JsonElement> InvokeAsync(
        RouteResolution route,
        string action,
        Dictionary<string, JsonElement>? requestPayload,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();

        var proxyRequest = BuildProxyRequest(route, action, requestPayload);
        ApplyServiceHeaders(proxyRequest, idempotencyKey);

        var response = await httpClient.SendAsync(proxyRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var statusCode = response.StatusCode == HttpStatusCode.Conflict
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status502BadGateway;

            var errorCode = response.StatusCode == HttpStatusCode.Conflict
                ? ApiErrorCodes.Conflict
                : ApiErrorCodes.InternalError;

            throw new ApiErrorException(
                statusCode,
                errorCode,
                "Worker API недоступен или вернул ошибку.",
                new Dictionary<string, object?>
                {
                    ["statusCode"] = (int)response.StatusCode,
                    ["body"] = payload,
                });
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
        }

        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }

    private HttpRequestMessage BuildProxyRequest(
        RouteResolution route,
        string action,
        Dictionary<string, JsonElement>? requestPayload)
    {
        var (method, path) = ResolveWorkerEndpoint(action, requestPayload);
        var absoluteUri = BuildAbsoluteUri(route, path);

        var request = new HttpRequestMessage(method, absoluteUri);
        if (requestPayload is not null)
        {
            request.Content = JsonContent.Create(requestPayload);
        }

        return request;
    }

    private (HttpMethod Method, string Path) ResolveWorkerEndpoint(string action, Dictionary<string, JsonElement>? requestPayload)
    {
        if (action.StartsWith("ext.", StringComparison.Ordinal))
        {
            return (HttpMethod.Post, BuildPath($"/actions/{Uri.EscapeDataString(action)}"));
        }

        if (string.Equals(action, "listings.search", StringComparison.Ordinal))
        {
            return (HttpMethod.Post, BuildPath("/listings/search"));
        }

        if (string.Equals(action, "messages.send", StringComparison.Ordinal))
        {
            return (HttpMethod.Post, BuildPath("/messages/send"));
        }

        if (string.Equals(action, "orders.search", StringComparison.Ordinal))
        {
            return (HttpMethod.Post, BuildPath("/orders/search"));
        }

        if (action.StartsWith("orders.", StringComparison.Ordinal) && TryReadPathId(requestPayload, "orderId", out var orderId))
        {
            return (
                HttpMethod.Post,
                BuildPath($"/orders/{Uri.EscapeDataString(orderId)}/actions/{Uri.EscapeDataString(action)}"));
        }

        return (HttpMethod.Post, BuildPath($"/actions/{Uri.EscapeDataString(action)}"));
    }

    private static bool TryReadPathId(Dictionary<string, JsonElement>? payload, string key, out string id)
    {
        id = string.Empty;
        if (payload is null || !payload.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value.GetString()))
        {
            return false;
        }

        id = value.GetString()!.Trim();
        return true;
    }

    private Uri BuildAbsoluteUri(RouteResolution route, string path)
    {
        var baseAddress = _options.BaseUrlTemplate;
        baseAddress = baseAddress.Replace("{serverId}", route.WorkerBinding.ServerId, StringComparison.OrdinalIgnoreCase);
        baseAddress = baseAddress.Replace("{workerId}", route.WorkerBinding.WorkerId, StringComparison.OrdinalIgnoreCase);
        baseAddress = baseAddress.Replace("{podId}", route.WorkerBinding.PodId ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out var baseUri))
        {
            throw new ApiErrorException(
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.InternalError,
                "Некорректная конфигурация WorkerProxyClient.BaseUrlTemplate.");
        }

        return new Uri(baseUri, path);
    }

    private string BuildPath(string suffix)
    {
        var prefix = _options.PathPrefix.TrimEnd('/');
        return $"{prefix}{suffix}";
    }

    private void EnsureEnabled()
    {
        if (_options.Enabled)
        {
            return;
        }

        throw new ApiErrorException(
            StatusCodes.Status501NotImplemented,
            ApiErrorCodes.FeatureNotReady,
            "Worker proxy отключен в текущем профиле.");
    }

    private void ApplyServiceHeaders(HttpRequestMessage request, string? idempotencyKey)
    {
        if (!string.IsNullOrWhiteSpace(_options.ServiceToken))
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.ServiceToken, _options.ServiceToken);
        }

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.IdempotencyKey, idempotencyKey);
        }
    }
}
