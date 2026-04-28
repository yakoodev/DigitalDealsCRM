namespace DDCRM.Core.Api.Integrations;

public interface IEntitlementCheckClient
{
    Task<bool> IsAllowedAsync(Guid projectId, Guid userId, string action, CancellationToken cancellationToken);
}
