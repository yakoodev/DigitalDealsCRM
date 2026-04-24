namespace DDCRM.Core.Api.AccountsManager;

public interface IAccountsManagerClient
{
    Task CreateLifecycleAsync(
        Guid projectId,
        Guid accountId,
        string platform,
        IDictionary<string, object?> proxyConfig,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task UpdateLifecycleAsync(
        Guid accountId,
        IDictionary<string, object?> proxyConfig,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task DeleteLifecycleAsync(
        Guid accountId,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
