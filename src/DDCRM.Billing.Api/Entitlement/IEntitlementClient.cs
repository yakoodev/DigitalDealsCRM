namespace DDCRM.Billing.Api.Entitlement;

public interface IEntitlementClient
{
    Task RecalculateAsync(
        Guid projectId,
        string subscriptionStatus,
        string planKey,
        string source,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
