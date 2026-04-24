using System.Text.Json;
using DDCRM.Gateway.Api.Clients;

namespace DDCRM.Gateway.Api.Tests.Infrastructure;

public sealed class FakeWorkerProxyClient : IWorkerProxyClient
{
    public bool NextCapabilitySupported { get; set; } = true;

    public JsonElement NextPayload { get; set; } = JsonSerializer.SerializeToElement(new
    {
        result = new
        {
            ok = true,
        },
    });

    public CapabilityCheckCall? LastCapabilityCheck { get; private set; }

    public InvocationCall? LastInvocation { get; private set; }

    public Task<bool> SupportsActionAsync(RouteResolution route, string action, CancellationToken cancellationToken)
    {
        LastCapabilityCheck = new CapabilityCheckCall(route, action);
        return Task.FromResult(NextCapabilitySupported);
    }

    public Task<JsonElement> InvokeAsync(
        RouteResolution route,
        string action,
        Dictionary<string, JsonElement>? request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        LastInvocation = new InvocationCall(route, action, request, idempotencyKey);
        return Task.FromResult(NextPayload);
    }
}

public sealed record CapabilityCheckCall(RouteResolution Route, string Action);

public sealed record InvocationCall(
    RouteResolution Route,
    string Action,
    Dictionary<string, JsonElement>? Request,
    string IdempotencyKey);
