using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDCRM.Shared.Constants;
using DDCRM.Shared.Errors;

namespace DDCRM.Core.Api.Integrations;

public abstract class ProjectServiceIntegrationHttpClientBase(HttpClient httpClient)
{
    protected async Task UpsertProjectTokenCoreAsync(
        string? serviceToken,
        Guid projectId,
        string projectToken,
        IReadOnlyCollection<string> scopes,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/ddcrm/project-tokens/upsert")
        {
            Content = JsonContent.Create(new
            {
                projectId,
                token = projectToken,
                scopes = scopes.ToArray(),
            }),
        };

        ApplyStandardHeaders(request, serviceToken, idempotencyKey);
        await EnsureSuccessAsync(request, "upsertProjectToken", cancellationToken);
    }

    protected async Task RevokeProjectTokenCoreAsync(
        string? serviceToken,
        Guid projectId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/ddcrm/project-tokens/revoke")
        {
            Content = JsonContent.Create(new
            {
                projectId,
            }),
        };

        ApplyStandardHeaders(request, serviceToken, idempotencyKey);
        await EnsureSuccessAsync(request, "revokeProjectToken", cancellationToken);
    }

    protected async Task<JsonElement> InvokeCoreAsync(
        string? serviceToken,
        Guid projectId,
        string scope,
        Dictionary<string, JsonElement>? payload,
        string projectToken,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/internal/v1/ddcrm/integration/{Uri.EscapeDataString(scope)}")
        {
            Content = JsonContent.Create(new
            {
                projectId,
                payload,
            }),
        };

        ApplyStandardHeaders(request, serviceToken, idempotencyKey);
        request.Headers.TryAddWithoutValidation(HeaderNames.ProjectServiceToken, projectToken);

        var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw CreateUpstreamError(response.StatusCode, body, $"invoke.{scope}");
        }

        var data = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(cancellationToken);
        if (data is null || !data.TryGetValue("result", out var result))
        {
            return JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["status"] = "ok",
            });
        }

        return result;
    }

    private async Task EnsureSuccessAsync(HttpRequestMessage request, string operation, CancellationToken cancellationToken)
    {
        var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw CreateUpstreamError(response.StatusCode, body, operation);
    }

    private static void ApplyStandardHeaders(HttpRequestMessage request, string? serviceToken, string idempotencyKey)
    {
        if (!string.IsNullOrWhiteSpace(serviceToken))
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.ServiceToken, serviceToken);
        }

        request.Headers.TryAddWithoutValidation(HeaderNames.IdempotencyKey, idempotencyKey);
    }

    private static ApiErrorException CreateUpstreamError(HttpStatusCode upstreamCode, string body, string operation)
    {
        return (int)upstreamCode switch
        {
            StatusCodes.Status400BadRequest => new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                $"Сервисная интеграция вернула ошибку валидации на операции {operation}.",
                CreateDetails(upstreamCode, body)),

            StatusCodes.Status403Forbidden => new ApiErrorException(
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.Forbidden,
                $"Сервис отклонил интеграционный вызов на операции {operation}.",
                CreateDetails(upstreamCode, body)),

            StatusCodes.Status404NotFound => new ApiErrorException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound,
                $"Сервис не нашёл ресурс на операции {operation}.",
                CreateDetails(upstreamCode, body)),

            _ => new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                $"Сервисная интеграция завершилась ошибкой на операции {operation}.",
                CreateDetails(upstreamCode, body)),
        };
    }

    private static Dictionary<string, object?> CreateDetails(HttpStatusCode upstreamCode, string body)
    {
        return new Dictionary<string, object?>
        {
            ["upstreamStatusCode"] = (int)upstreamCode,
            ["upstreamBody"] = string.IsNullOrWhiteSpace(body) ? null : body,
        };
    }
}
