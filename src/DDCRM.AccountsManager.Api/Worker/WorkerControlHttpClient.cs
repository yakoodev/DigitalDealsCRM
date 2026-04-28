using System.Net.Http.Json;
using DDCRM.AccountsManager.Api.RouteRegistry;
using DDCRM.Shared.Constants;
using DDCRM.Shared.Errors;
using Microsoft.Extensions.Options;

namespace DDCRM.AccountsManager.Api.Worker;

public sealed class WorkerControlHttpClient(
    HttpClient httpClient,
    IOptions<WorkerControlClientOptions> options)
    : IWorkerControlClient
{
    private readonly WorkerControlClientOptions _options = options.Value;

    public async Task ApplyProxyCredentialsAsync(
        WorkerBindingDto workerBinding,
        Guid accountId,
        Dictionary<string, object?> proxyConfig,
        string idempotencyKey,
        string? baseUrlTemplateOverride,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled())
        {
            return;
        }

        var absoluteUri = BuildAbsoluteUri(
            workerBinding,
            BuildPath("/actions/ext.account.proxy-credentials.apply"),
            baseUrlTemplateOverride);
        var payload = new Dictionary<string, object?>
        {
            ["accountId"] = accountId,
            ["proxyConfig"] = proxyConfig,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, absoluteUri)
        {
            Content = JsonContent.Create(new Dictionary<string, object?>
            {
                ["payload"] = payload,
            }),
        };

        ApplyHeaders(request, idempotencyKey);

        var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new ApiErrorException(
            StatusCodes.Status502BadGateway,
            ApiErrorCodes.InternalError,
            "Worker control API недоступен или вернул ошибку при apply proxy credentials.",
            new Dictionary<string, object?>
            {
                ["statusCode"] = (int)response.StatusCode,
                ["body"] = body,
            });
    }

    private bool IsEnabled() => _options.Enabled;

    private void ApplyHeaders(HttpRequestMessage message, string idempotencyKey)
    {
        if (!string.IsNullOrWhiteSpace(_options.ServiceToken))
        {
            message.Headers.TryAddWithoutValidation(HeaderNames.ServiceToken, _options.ServiceToken);
        }

        message.Headers.TryAddWithoutValidation(HeaderNames.IdempotencyKey, idempotencyKey);
    }

    private Uri BuildAbsoluteUri(
        WorkerBindingDto workerBinding,
        string path,
        string? baseUrlTemplateOverride)
    {
        var baseAddress = !string.IsNullOrWhiteSpace(baseUrlTemplateOverride)
            ? baseUrlTemplateOverride
            : _options.BaseUrlTemplate;

        if (string.IsNullOrWhiteSpace(baseAddress))
        {
            throw new ApiErrorException(
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.InternalError,
                "Не задана конфигурация WorkerControlClient.BaseUrlTemplate.");
        }

        baseAddress = baseAddress.Replace("{serverId}", workerBinding.ServerId, StringComparison.OrdinalIgnoreCase);
        baseAddress = baseAddress.Replace("{workerId}", workerBinding.WorkerId, StringComparison.OrdinalIgnoreCase);
        baseAddress = baseAddress.Replace("{podId}", workerBinding.PodId ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out var baseUri))
        {
            throw new ApiErrorException(
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.InternalError,
                "Некорректная конфигурация WorkerControlClient.BaseUrlTemplate.");
        }

        return new Uri(baseUri, path);
    }

    private string BuildPath(string suffix)
    {
        var prefix = _options.PathPrefix.TrimEnd('/');
        return $"{prefix}{suffix}";
    }
}
