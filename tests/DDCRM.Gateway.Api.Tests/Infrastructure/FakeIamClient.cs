using DDCRM.Gateway.Api.Clients;

namespace DDCRM.Gateway.Api.Tests.Infrastructure;

public sealed class FakeIamClient : IIamClient
{
    public bool NextAllowed { get; set; } = true;

    public PermissionCheckCall? LastCall { get; private set; }

    public Task<bool> CheckPermissionAsync(Guid projectId, Guid userId, string permission, CancellationToken cancellationToken)
    {
        LastCall = new PermissionCheckCall(projectId, userId, permission);
        return Task.FromResult(NextAllowed);
    }
}

public sealed record PermissionCheckCall(Guid ProjectId, Guid UserId, string Permission);
