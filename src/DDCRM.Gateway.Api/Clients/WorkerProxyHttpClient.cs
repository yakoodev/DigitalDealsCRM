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
        var isExtensionAction = action.StartsWith("ext.", StringComparison.Ordinal);
        var (method, path) = ResolveWorkerEndpoint(action, requestPayload);
        var absoluteUri = BuildAbsoluteUri(route, path);
        var payload = SanitizeRequestPayload(action, requestPayload);

        var request = new HttpRequestMessage(method, absoluteUri);
        if (isExtensionAction)
        {
            // Worker extension contract expects body shape: { payload: { ... } }.
            request.Content = JsonContent.Create(new ExtensionActionProxyRequest(requestPayload));
        }
        else if (payload is not null && method != HttpMethod.Get)
        {
            request.Content = JsonContent.Create(payload);
        }

        return request;
    }

    private (HttpMethod Method, string Path) ResolveWorkerEndpoint(string action, Dictionary<string, JsonElement>? requestPayload)
    {
        if (action.StartsWith("ext.", StringComparison.Ordinal))
        {
            return (HttpMethod.Post, BuildPath($"/actions/{Uri.EscapeDataString(action)}"));
        }

        if (string.Equals(action, "account.info", StringComparison.Ordinal))
        {
            return (HttpMethod.Get, BuildPath("/account"));
        }

        if (string.Equals(action, "conversations.list", StringComparison.Ordinal))
        {
            var query = BuildQueryString(requestPayload, "limit", "cursor", "onlyUnread");
            return (HttpMethod.Get, BuildPath($"/conversations{query}"));
        }

        if (string.Equals(action, "conversations.messages.list", StringComparison.Ordinal))
        {
            var conversationId = ReadRequiredPathId(requestPayload, "conversationId");
            var query = BuildQueryString(requestPayload, "limit", "cursor");
            return (HttpMethod.Get, BuildPath($"/conversations/{Uri.EscapeDataString(conversationId)}/messages{query}"));
        }

        if (string.Equals(action, "conversations.messages.send", StringComparison.Ordinal))
        {
            var conversationId = ReadRequiredPathId(requestPayload, "conversationId");
            return (HttpMethod.Post, BuildPath($"/conversations/{Uri.EscapeDataString(conversationId)}/messages"));
        }

        if (string.Equals(action, "products.list", StringComparison.Ordinal))
        {
            var query = BuildQueryString(requestPayload, "status", "limit", "cursor");
            return (HttpMethod.Get, BuildPath($"/products{query}"));
        }

        if (string.Equals(action, "products.create", StringComparison.Ordinal))
        {
            return (HttpMethod.Post, BuildPath("/products"));
        }

        if (string.Equals(action, "products.update", StringComparison.Ordinal))
        {
            var productId = ReadRequiredPathId(requestPayload, "productId");
            return (HttpMethod.Patch, BuildPath($"/products/{Uri.EscapeDataString(productId)}"));
        }

        if (string.Equals(action, "products.delete", StringComparison.Ordinal))
        {
            var productId = ReadRequiredPathId(requestPayload, "productId");
            return (HttpMethod.Delete, BuildPath($"/products/{Uri.EscapeDataString(productId)}"));
        }

        if (string.Equals(action, "products.schemas.list", StringComparison.Ordinal)
            || string.Equals(action, "products.schemas", StringComparison.Ordinal))
        {
            var query = BuildQueryString(requestPayload, "schemaId");
            return (HttpMethod.Get, BuildPath($"/schemas/products{query}"));
        }

        return (HttpMethod.Post, BuildPath($"/actions/{Uri.EscapeDataString(action)}"));
    }

    private static Dictionary<string, JsonElement>? SanitizeRequestPayload(
        string action,
        Dictionary<string, JsonElement>? requestPayload)
    {
        if (requestPayload is null)
        {
            return null;
        }

        if (string.Equals(action, "conversations.messages.send", StringComparison.Ordinal))
        {
            return RemovePayloadKeys(requestPayload, "conversationId");
        }

        if (string.Equals(action, "products.update", StringComparison.Ordinal)
            || string.Equals(action, "products.delete", StringComparison.Ordinal))
        {
            return RemovePayloadKeys(requestPayload, "productId");
        }

        return requestPayload;
    }

    private static Dictionary<string, JsonElement> RemovePayloadKeys(
        Dictionary<string, JsonElement> payload,
        params string[] keysToRemove)
    {
        var keys = keysToRemove.ToHashSet(StringComparer.Ordinal);
        return payload
            .Where(x => !keys.Contains(x.Key))
            .ToDictionary(x => x.Key, x => x.Value.Clone(), StringComparer.Ordinal);
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

    private static string ReadRequiredPathId(Dictionary<string, JsonElement>? payload, string key)
    {
        if (TryReadPathId(payload, key, out var id))
        {
            return id;
        }

        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            $"Для action требуется payload.{key}.");
    }

    private static string BuildQueryString(Dictionary<string, JsonElement>? payload, params string[] keys)
    {
        if (payload is null || payload.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var key in keys)
        {
            if (!payload.TryGetValue(key, out var value) || !TryReadQueryValue(value, out var rawValue))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(rawValue))
            {
                continue;
            }

            parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(rawValue)}");
        }

        return parts.Count == 0
            ? string.Empty
            : $"?{string.Join("&", parts)}";
    }

    private static bool TryReadQueryValue(JsonElement value, out string rawValue)
    {
        rawValue = string.Empty;
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                rawValue = value.GetString() ?? string.Empty;
                return true;
            case JsonValueKind.Number:
                rawValue = value.GetRawText();
                return true;
            case JsonValueKind.True:
                rawValue = "true";
                return true;
            case JsonValueKind.False:
                rawValue = "false";
                return true;
            default:
                return false;
        }
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

    private sealed record ExtensionActionProxyRequest(Dictionary<string, JsonElement>? Payload);
}
