using System.Text.Json;

namespace DDCRM.Gateway.Api.Clients;

public interface IWorkerProxyClient
{
    Task<bool> SupportsActionAsync(RouteResolution route, string action, CancellationToken cancellationToken);

    Task<JsonElement> InvokeAsync(
        RouteResolution route,
        string action,
        Dictionary<string, JsonElement>? request,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
