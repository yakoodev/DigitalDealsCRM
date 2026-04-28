using System.Net.Http.Json;
using System.Text.Json;
using DDCRM.Shared.Constants;
using Microsoft.Extensions.Options;

namespace DDCRM.Core.Api.Integrations;

public sealed class EntitlementCheckHttpClient(
    HttpClient httpClient,
    IOptions<EntitlementCheckClientOptions> options)
    : IEntitlementCheckClient
{
    private readonly EntitlementCheckClientOptions _options = options.Value;

    public async Task<bool> IsAllowedAsync(Guid projectId, Guid userId, string action, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/entitlement/check")
        {
            Content = JsonContent.Create(new
            {
                projectId,
                userId,
                action,
            }),
        };

        if (!string.IsNullOrWhiteSpace(_options.ServiceToken))
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.ServiceToken, _options.ServiceToken);
        }

        var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(cancellationToken);
        if (payload is null || !payload.TryGetValue("data", out var dataElement))
        {
            return false;
        }

        if (dataElement.ValueKind != JsonValueKind.Object
            || !dataElement.TryGetProperty("allowed", out var allowedElement))
        {
            return false;
        }

        return allowedElement.ValueKind == JsonValueKind.True;
    }
}
