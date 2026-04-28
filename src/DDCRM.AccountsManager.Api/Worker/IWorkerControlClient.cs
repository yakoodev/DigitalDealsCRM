using DDCRM.AccountsManager.Api.RouteRegistry;

namespace DDCRM.AccountsManager.Api.Worker;

public interface IWorkerControlClient
{
    Task ApplyProxyCredentialsAsync(
        WorkerBindingDto workerBinding,
        Guid accountId,
        Dictionary<string, object?> proxyConfig,
        string idempotencyKey,
        string? baseUrlTemplateOverride,
        CancellationToken cancellationToken);
}
