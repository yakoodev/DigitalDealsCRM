using System.Text.Json;

namespace DDCRM.Core.Api.GatewayProxy;

public interface IGatewayProxyClient
{
    Task<JsonElement> InvokeAccountApiActionAsync(
        string routeKey,
        string action,
        IDictionary<string, JsonElement>? payload,
        string authorizationHeader,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
