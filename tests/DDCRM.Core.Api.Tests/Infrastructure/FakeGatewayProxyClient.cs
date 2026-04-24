using System.Text.Json;
using DDCRM.Core.Api.GatewayProxy;

namespace DDCRM.Core.Api.Tests.Infrastructure;

public sealed class FakeGatewayProxyClient : IGatewayProxyClient
{
    private const string ProxyCredentialsRevealAction = "ext.account.proxy-credentials.reveal";

    public List<GatewayProxyCall> Calls { get; } = [];

    public Task<JsonElement> InvokeAccountApiActionAsync(
        string routeKey,
        string action,
        IDictionary<string, JsonElement>? payload,
        string authorizationHeader,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var payloadData = payload is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : payload.ToDictionary(x => x.Key, x => ConvertElement(x.Value), StringComparer.Ordinal);

        Calls.Add(new GatewayProxyCall(routeKey, action, authorizationHeader, idempotencyKey, payloadData));

        if (string.Equals(action, ProxyCredentialsRevealAction, StringComparison.Ordinal))
        {
            var suffix = payloadData.TryGetValue("accountId", out var accountIdValue)
                ? accountIdValue?.ToString() ?? "unknown"
                : "unknown";

            var secretSuffix = suffix.Length >= 6 ? suffix[..6] : suffix;

            var revealResult = new Dictionary<string, object?>
            {
                ["proxyConfig"] = new Dictionary<string, object?>
                {
                    ["host"] = "proxy.reveal.internal",
                    ["port"] = 8443,
                    ["login"] = "reveal-login",
                    ["password"] = $"secret-{secretSuffix}",
                },
            };

            return Task.FromResult(JsonSerializer.SerializeToElement(revealResult));
        }

        var result = new Dictionary<string, object?>
        {
            ["routeKey"] = routeKey,
            ["action"] = action,
            ["idempotencyKey"] = idempotencyKey,
            ["echo"] = payloadData,
        };

        return Task.FromResult(JsonSerializer.SerializeToElement(result));
    }

    public void Reset() => Calls.Clear();

    private static object? ConvertElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var intValue) => intValue,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(x => x.Name, x => ConvertElement(x.Value), StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertElement).ToArray(),
            _ => element.GetRawText(),
        };
    }
}

public sealed record GatewayProxyCall(
    string RouteKey,
    string Action,
    string AuthorizationHeader,
    string IdempotencyKey,
    IReadOnlyDictionary<string, object?> Payload);
