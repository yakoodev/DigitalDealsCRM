using System.Net.Http.Json;
using DDCRM.Shared.Constants;
using DDCRM.Shared.Errors;
using Microsoft.Extensions.Options;

namespace DDCRM.AccountsManager.Api.RouteRegistry;

public sealed class RouteRegistryHttpClient(
    HttpClient httpClient,
    IOptions<RouteRegistryClientOptions> options)
    : IRouteRegistryClient
{
    private readonly RouteRegistryClientOptions _options = options.Value;

    public async Task UpsertAsync(Guid accountId, RouteUpsertRequestDto request, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (!IsEnabled())
        {
            return;
        }

        using var message = new HttpRequestMessage(HttpMethod.Put, $"/internal/v1/routes/{accountId}")
        {
            Content = JsonContent.Create(request),
        };
        ApplyHeaders(message, idempotencyKey);
        await SendAsync(message, cancellationToken);
    }

    public async Task DeleteAsync(Guid accountId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (!IsEnabled())
        {
            return;
        }

        using var message = new HttpRequestMessage(HttpMethod.Delete, $"/internal/v1/routes/{accountId}");
        ApplyHeaders(message, idempotencyKey);
        await SendAsync(message, cancellationToken);
    }

    public async Task SwitchAsync(Guid accountId, RouteSwitchRequestDto request, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (!IsEnabled())
        {
            return;
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, $"/internal/v1/routes/{accountId}/switch")
        {
            Content = JsonContent.Create(request),
        };
        ApplyHeaders(message, idempotencyKey);
        await SendAsync(message, cancellationToken);
    }

    private bool IsEnabled() => _options.Enabled && !string.IsNullOrWhiteSpace(_options.BaseUrl);

    private void ApplyHeaders(HttpRequestMessage message, string idempotencyKey)
    {
        if (!string.IsNullOrWhiteSpace(_options.ServiceToken))
        {
            message.Headers.TryAddWithoutValidation(HeaderNames.ServiceToken, _options.ServiceToken);
        }

        message.Headers.TryAddWithoutValidation(HeaderNames.IdempotencyKey, idempotencyKey);
    }

    private async Task SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        var response = await httpClient.SendAsync(message, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

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
}
