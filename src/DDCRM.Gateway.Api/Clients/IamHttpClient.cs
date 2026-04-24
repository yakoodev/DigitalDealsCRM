using System.Net.Http.Json;
using System.Text.Json;
using DDCRM.Shared.Constants;
using DDCRM.Shared.Errors;
using Microsoft.Extensions.Options;

namespace DDCRM.Gateway.Api.Clients;

public sealed class IamHttpClient(
    HttpClient httpClient,
    IOptions<IamClientOptions> options)
    : IIamClient
{
    private readonly IamClientOptions _options = options.Value;

    public async Task<bool> CheckPermissionAsync(Guid projectId, Guid userId, string permission, CancellationToken cancellationToken)
    {
        EnsureEnabled();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/iam/check-permission")
        {
            Content = JsonContent.Create(new
            {
                projectId,
                userId,
                permission,
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
                "IAM недоступен или вернул ошибку.",
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
            return false;
        }

        if (!dataElement.TryGetProperty("allowed", out var allowedElement))
        {
            return false;
        }

        return allowedElement.ValueKind == JsonValueKind.True;
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
            "Клиент IAM отключен.");
    }

    private void ApplyServiceToken(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_options.ServiceToken))
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.ServiceToken, _options.ServiceToken);
        }
    }
}
