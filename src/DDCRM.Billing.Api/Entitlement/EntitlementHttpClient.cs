using System.Net;
using System.Net.Http.Json;
using DDCRM.Shared.Constants;
using DDCRM.Shared.Errors;
using Microsoft.Extensions.Options;

namespace DDCRM.Billing.Api.Entitlement;

public sealed class EntitlementHttpClient(
    HttpClient httpClient,
    IOptions<EntitlementClientOptions> options)
    : IEntitlementClient
{
    private readonly EntitlementClientOptions _options = options.Value;

    public async Task RecalculateAsync(
        Guid projectId,
        string subscriptionStatus,
        string planKey,
        string source,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/entitlement/recalculate")
        {
            Content = JsonContent.Create(new
            {
                projectId,
                subscriptionStatus,
                planKey,
                source,
            }),
        };

        ApplyHeaders(request, idempotencyKey);

        var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw CreateUpstreamError(response.StatusCode, body);
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
            "Entitlement client отключен в текущем runtime-профиле.");
    }

    private void ApplyHeaders(HttpRequestMessage request, string idempotencyKey)
    {
        if (!string.IsNullOrWhiteSpace(_options.ServiceToken))
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.ServiceToken, _options.ServiceToken);
        }

        request.Headers.TryAddWithoutValidation(HeaderNames.IdempotencyKey, idempotencyKey);
    }

    private static ApiErrorException CreateUpstreamError(HttpStatusCode statusCode, string body)
    {
        return new ApiErrorException(
            StatusCodes.Status502BadGateway,
            ApiErrorCodes.InternalError,
            "Entitlement recalculate завершился ошибкой.",
            new Dictionary<string, object?>
            {
                ["upstreamStatusCode"] = (int)statusCode,
                ["upstreamBody"] = body,
            });
    }
}
