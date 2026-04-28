using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DDCRM.Core.Api.Integrations;

public sealed class FunPayStatIntegrationClient(
    HttpClient httpClient,
    IOptions<FunPayStatClientOptions> options)
    : ProjectServiceIntegrationHttpClientBase(httpClient), IProjectServiceIntegrationClient
{
    private readonly FunPayStatClientOptions _options = options.Value;

    public string IntegrationKey => IntegrationKeys.FunPayStat;

    public Task UpsertProjectTokenAsync(
        Guid projectId,
        string projectToken,
        IReadOnlyCollection<string> scopes,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        return UpsertProjectTokenCoreAsync(_options.ServiceToken, projectId, projectToken, scopes, idempotencyKey, cancellationToken);
    }

    public Task RevokeProjectTokenAsync(
        Guid projectId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        return RevokeProjectTokenCoreAsync(_options.ServiceToken, projectId, idempotencyKey, cancellationToken);
    }

    public Task<JsonElement> InvokeAsync(
        Guid projectId,
        string scope,
        Dictionary<string, JsonElement>? payload,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        if (payload is null || !payload.TryGetValue("projectToken", out var tokenElement))
        {
            throw new InvalidOperationException("Отсутствует projectToken для сервисной интеграции.");
        }

        var projectToken = tokenElement.GetString();
        if (string.IsNullOrWhiteSpace(projectToken))
        {
            throw new InvalidOperationException("projectToken должен быть непустой строкой.");
        }

        payload.Remove("projectToken");
        return InvokeCoreAsync(_options.ServiceToken, projectId, scope, payload, projectToken, idempotencyKey, cancellationToken);
    }

    private void EnsureEnabled()
    {
        if (_options.Enabled)
        {
            return;
        }

        throw new InvalidOperationException("FunPayStat integration client отключен.");
    }
}
