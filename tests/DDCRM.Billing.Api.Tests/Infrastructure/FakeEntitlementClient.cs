using DDCRM.Billing.Api.Entitlement;

namespace DDCRM.Billing.Api.Tests.Infrastructure;

public sealed class FakeEntitlementClient : IEntitlementClient
{
    public List<RecalculateCall> Calls { get; } = [];

    public Task RecalculateAsync(
        Guid projectId,
        string subscriptionStatus,
        string planKey,
        string source,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        Calls.Add(new RecalculateCall(projectId, subscriptionStatus, planKey, source, idempotencyKey));
        return Task.CompletedTask;
    }

    public void Reset() => Calls.Clear();
}

public sealed record RecalculateCall(
    Guid ProjectId,
    string SubscriptionStatus,
    string PlanKey,
    string Source,
    string IdempotencyKey);
