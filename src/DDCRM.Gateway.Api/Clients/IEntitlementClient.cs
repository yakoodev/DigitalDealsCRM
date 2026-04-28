namespace DDCRM.Gateway.Api.Clients;

public interface IEntitlementClient
{
    Task<bool> IsAllowedAsync(Guid projectId, Guid userId, string action, CancellationToken cancellationToken);
}
