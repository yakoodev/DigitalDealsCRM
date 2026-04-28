using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDCRM.Shared.Constants;
using DDCRM.Shared.Errors;
using Microsoft.Extensions.Options;

namespace DDCRM.Gateway.Api.Clients;

public sealed class EntitlementHttpClient(
    HttpClient httpClient,
    IOptions<EntitlementClientOptions> options)
    : IEntitlementClient
{
    private readonly EntitlementClientOptions _options = options.Value;

    public async Task<bool> IsAllowedAsync(Guid projectId, Guid userId, string action, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return true;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/entitlement/check")
        {
            Content = JsonContent.Create(new
            {
                projectId,
                userId,
                action,
            }),
        };

        ApplyServiceToken(request);

        var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotImplemented)
        {
            return true;
        }

        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                "Entitlement недоступен или вернул ошибку.",
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
            return true;
        }

        if (dataElement.TryGetProperty("allowed", out var allowedElement))
        {
            return allowedElement.ValueKind == JsonValueKind.True;
        }

        if (dataElement.TryGetProperty("blocked", out var blockedElement))
        {
            return blockedElement.ValueKind != JsonValueKind.True;
        }

        return true;
    }

    private void ApplyServiceToken(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_options.ServiceToken))
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.ServiceToken, _options.ServiceToken);
        }
    }
}
