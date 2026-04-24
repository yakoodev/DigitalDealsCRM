using DDCRM.Gateway.Api.Clients;

namespace DDCRM.Gateway.Api.Tests.Infrastructure;

public sealed class FakeEntitlementClient : IEntitlementClient
{
    public bool NextAllowed { get; set; } = true;

    public EntitlementCheckCall? LastCall { get; private set; }

    public Task<bool> IsAllowedAsync(Guid projectId, Guid userId, string action, CancellationToken cancellationToken)
    {
        LastCall = new EntitlementCheckCall(projectId, userId, action);
        return Task.FromResult(NextAllowed);
    }
}

public sealed record EntitlementCheckCall(Guid ProjectId, Guid UserId, string Action);
