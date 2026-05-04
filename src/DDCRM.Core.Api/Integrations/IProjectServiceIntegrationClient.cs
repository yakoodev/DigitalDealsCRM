using System.Text.Json;

namespace DDCRM.Core.Api.Integrations;

public interface IProjectServiceIntegrationClient
{
    string IntegrationKey { get; }

    Task UpsertProjectTokenAsync(
        Guid projectId,
        string projectToken,
        IReadOnlyCollection<string> scopes,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task RevokeProjectTokenAsync(
        Guid projectId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<JsonElement> InvokeAsync(
        Guid projectId,
        string scope,
        Dictionary<string, JsonElement>? payload,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
