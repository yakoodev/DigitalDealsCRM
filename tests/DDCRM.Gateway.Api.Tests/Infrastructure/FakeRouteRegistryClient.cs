using DDCRM.Gateway.Api.Clients;

namespace DDCRM.Gateway.Api.Tests.Infrastructure;

public sealed class FakeRouteRegistryClient : IRouteRegistryClient
{
    public RouteResolution? NextRoute { get; set; }

    public string? LastRouteKey { get; private set; }

    public Task<RouteResolution?> ResolveAsync(string routeKey, CancellationToken cancellationToken)
    {
        LastRouteKey = routeKey;
        return Task.FromResult(NextRoute);
    }
}
