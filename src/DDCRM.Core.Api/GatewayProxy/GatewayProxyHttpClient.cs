using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DDCRM.Shared.Constants;
using DDCRM.Shared.Errors;
using Microsoft.Extensions.Options;

namespace DDCRM.Core.Api.GatewayProxy;

public sealed class GatewayProxyHttpClient(
    HttpClient httpClient,
    IOptions<GatewayProxyClientOptions> options)
    : IGatewayProxyClient
{
    private readonly GatewayProxyClientOptions _options = options.Value;

    public async Task<JsonElement> InvokeAccountApiActionAsync(
        string routeKey,
        string action,
        IDictionary<string, JsonElement>? payload,
        string authorizationHeader,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();

        var path = $"/v1/account-api/{Uri.EscapeDataString(routeKey)}/{Uri.EscapeDataString(action)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = payload is null ? null : JsonContent.Create(payload),
        };

        request.Headers.TryAddWithoutValidation(HeaderNames.IdempotencyKey, idempotencyKey);

        if (!AuthenticationHeaderValue.TryParse(authorizationHeader, out var parsedHeader))
        {
            throw new ApiErrorException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.Unauthorized,
                "Некорректный заголовок Authorization.");
        }

        request.Headers.Authorization = parsedHeader;

        var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateUpstreamError(response.StatusCode, body);
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("result", out var resultElement) || resultElement.ValueKind != JsonValueKind.Object)
        {
            throw new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                "Gateway вернул некорректный формат ответа для proxyAccountApiAction.");
        }

        return resultElement.Clone();
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
            "Gateway proxy client отключен в текущем runtime-профиле.");
    }

    private static ApiErrorException CreateUpstreamError(HttpStatusCode statusCode, string body)
    {
        return (int)statusCode switch
        {
            StatusCodes.Status400BadRequest => new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "Gateway proxy вернул ошибку валидации.",
                CreateDetails(statusCode, body)),

            StatusCodes.Status401Unauthorized => new ApiErrorException(
                StatusCodes.Status401Unauthorized,
                ApiErrorCodes.Unauthorized,
                "Gateway proxy отклонил авторизацию.",
                CreateDetails(statusCode, body)),

            StatusCodes.Status403Forbidden => new ApiErrorException(
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.Forbidden,
                "Gateway proxy отклонил запрос по политике доступа.",
                CreateDetails(statusCode, body)),

            StatusCodes.Status404NotFound => new ApiErrorException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound,
                "Gateway proxy не нашёл маршрут.",
                CreateDetails(statusCode, body)),

            StatusCodes.Status409Conflict => new ApiErrorException(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.Conflict,
                "Gateway proxy вернул конфликт.",
                CreateDetails(statusCode, body)),

            _ => new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                "Gateway proxy завершился ошибкой.",
                CreateDetails(statusCode, body)),
        };
    }

    private static Dictionary<string, object?> CreateDetails(HttpStatusCode statusCode, string body)
    {
        return new Dictionary<string, object?>
        {
            ["upstreamStatusCode"] = (int)statusCode,
            ["upstreamBody"] = body,
        };
    }
}
