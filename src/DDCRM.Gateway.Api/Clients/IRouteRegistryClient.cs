namespace DDCRM.Gateway.Api.Clients;

public interface IRouteRegistryClient
{
    Task<RouteResolution?> ResolveAsync(string routeKey, CancellationToken cancellationToken);
}
