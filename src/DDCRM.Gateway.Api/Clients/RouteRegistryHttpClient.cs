using System.Net.Http.Json;
using System.Text.Json;
using DDCRM.Shared.Constants;
using DDCRM.Shared.Errors;
using Microsoft.Extensions.Options;

namespace DDCRM.Gateway.Api.Clients;

public sealed class RouteRegistryHttpClient(
    HttpClient httpClient,
    IOptions<RouteRegistryClientOptions> options)
    : IRouteRegistryClient
{
    private readonly RouteRegistryClientOptions _options = options.Value;

    public async Task<RouteResolution?> ResolveAsync(string routeKey, CancellationToken cancellationToken)
    {
        EnsureEnabled();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/routes/resolve-bulk")
        {
            Content = JsonContent.Create(new
            {
                routeKeys = new[] { routeKey },
            }),
        };

        ApplyServiceToken(request);

        var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                "Route Registry недоступен или вернул ошибку.",
                new Dictionary<string, object?>
                {
                    ["statusCode"] = (int)response.StatusCode,
                    ["body"] = payload,
                });
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("data", out var dataElement))
        {
            return null;
        }

        if (!dataElement.TryGetProperty("items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in itemsElement.EnumerateArray())
        {
            if (!item.TryGetProperty("routeKey", out var routeKeyElement)
                || !string.Equals(routeKeyElement.GetString(), routeKey, StringComparison.Ordinal))
            {
                continue;
            }

            return ParseRoute(item, routeKey);
        }

        return null;
    }

    private RouteResolution ParseRoute(JsonElement item, string expectedRouteKey)
    {
        var accountId = ParseGuid(item, "accountId");
        var projectId = ParseGuid(item, "projectId");
        var routeVersion = ParseInt(item, "routeVersion");

        if (!item.TryGetProperty("workerBinding", out var workerBindingElement) || workerBindingElement.ValueKind != JsonValueKind.Object)
        {
            throw new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                "Route Registry вернул некорректный workerBinding.");
        }

        var serverId = ParseString(workerBindingElement, "serverId");
        var workerId = ParseString(workerBindingElement, "workerId");
        var podId = workerBindingElement.TryGetProperty("podId", out var podIdElement)
            ? podIdElement.GetString()
            : null;

        return new RouteResolution(
            expectedRouteKey,
            accountId,
            projectId,
            routeVersion,
            new WorkerBinding(serverId, workerId, podId));
    }

    private static Guid ParseGuid(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || !Guid.TryParse(value.GetString(), out var parsed))
        {
            throw new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                $"Route Registry вернул некорректное поле {propertyName}.");
        }

        return parsed;
    }

    private static int ParseInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || !value.TryGetInt32(out var parsed))
        {
            throw new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                $"Route Registry вернул некорректное поле {propertyName}.");
        }

        return parsed;
    }

    private static string ParseString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                $"Route Registry вернул некорректное поле {propertyName}.");
        }

        return value.GetString()!.Trim();
    }

    private void EnsureEnabled()
    {
        if (_options.Enabled)
        {
            return;
        }

        throw new ApiErrorException(
            StatusCodes.Status503ServiceUnavailable,
            ApiErrorCodes.InternalError,
            "Клиент Route Registry отключен.");
    }

    private void ApplyServiceToken(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_options.ServiceToken))
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.ServiceToken, _options.ServiceToken);
        }
    }
}
