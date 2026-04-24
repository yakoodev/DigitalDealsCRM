namespace DDCRM.Gateway.Api.Clients;

public interface IIamClient
{
    Task<bool> CheckPermissionAsync(Guid projectId, Guid userId, string permission, CancellationToken cancellationToken);
}
