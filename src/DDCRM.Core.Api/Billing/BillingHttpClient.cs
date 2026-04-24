using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDCRM.Shared.Constants;
using DDCRM.Shared.Errors;
using Microsoft.Extensions.Options;

namespace DDCRM.Core.Api.Billing;

public sealed class BillingHttpClient(
    HttpClient httpClient,
    IOptions<BillingClientOptions> options)
    : IBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly BillingClientOptions _options = options.Value;

    public async Task<IDictionary<string, object?>> CreatePaymentAsync(
        Guid projectId,
        IDictionary<string, JsonElement>? payload,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();

        var body = MergePayload(payload, new Dictionary<string, object?>
        {
            ["projectId"] = projectId,
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/payments/create")
        {
            Content = JsonContent.Create(body),
        };

        ApplyHeaders(request, idempotencyKey);

        var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateUpstreamError(response.StatusCode, content, "Billing payment create завершился ошибкой.");
        }

        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Object)
        {
            throw new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                "Billing вернул некорректный формат ответа для payment create.");
        }

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(dataElement.GetRawText(), JsonOptions)
               ?? new Dictionary<string, object?>();
    }

    public async Task<string> ManualActivateSubscriptionAsync(
        Guid projectId,
        IDictionary<string, JsonElement>? payload,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();

        var body = MergePayload(payload, new Dictionary<string, object?>
        {
            ["projectId"] = projectId,
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/subscriptions/manual-activate")
        {
            Content = JsonContent.Create(body),
        };

        ApplyHeaders(request, idempotencyKey);

        var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateUpstreamError(response.StatusCode, content, "Billing manual activate завершился ошибкой.");
        }

        using var document = JsonDocument.Parse(content);
        return document.RootElement.TryGetProperty("status", out var statusElement)
            ? (statusElement.GetString() ?? "completed")
            : "completed";
    }

    private static Dictionary<string, object?> MergePayload(
        IDictionary<string, JsonElement>? payload,
        IDictionary<string, object?> additions)
    {
        var merged = payload is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : payload.ToDictionary(x => x.Key, x => (object?)x.Value.Clone(), StringComparer.Ordinal);

        foreach (var (key, value) in additions)
        {
            merged[key] = value;
        }

        return merged;
    }

    private void ApplyHeaders(HttpRequestMessage request, string idempotencyKey)
    {
        if (!string.IsNullOrWhiteSpace(_options.ServiceToken))
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.ServiceToken, _options.ServiceToken);
        }

        request.Headers.TryAddWithoutValidation(HeaderNames.IdempotencyKey, idempotencyKey);
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
            "Billing client отключен в текущем runtime-профиле.");
    }

    private static ApiErrorException CreateUpstreamError(HttpStatusCode upstreamCode, string body, string message)
    {
        if ((int)upstreamCode is >= 400 and < 500)
        {
            return new ApiErrorException(
                (int)upstreamCode,
                ApiErrorCodes.ValidationError,
                message,
                new Dictionary<string, object?>
                {
                    ["upstreamStatusCode"] = (int)upstreamCode,
                    ["upstreamBody"] = body,
                });
        }

        return new ApiErrorException(
            StatusCodes.Status502BadGateway,
            ApiErrorCodes.InternalError,
            message,
            new Dictionary<string, object?>
            {
                ["upstreamStatusCode"] = (int)upstreamCode,
                ["upstreamBody"] = body,
            });
    }
}
