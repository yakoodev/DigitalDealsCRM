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

    Task ApplyMarketplaceAuthAsync(
        WorkerBindingDto workerBinding,
        Guid accountId,
        MarketplaceAuthPayload marketplaceAuth,
        string idempotencyKey,
        string? baseUrlTemplateOverride,
        CancellationToken cancellationToken);

    Task ApplyMailConfigAsync(
        WorkerBindingDto workerBinding,
        Guid projectId,
        Guid accountId,
        MailConfigPayload mailConfig,
        string idempotencyKey,
        string? baseUrlTemplateOverride,
        CancellationToken cancellationToken);
}

public sealed record MarketplaceAuthPayload(
    string Scheme,
    IReadOnlyDictionary<string, string> Credentials);

public sealed record MailConfigPayload(
    bool Enabled,
    string ImapHost,
    int ImapPort,
    string ImapSecurity,
    string ImapUsername,
    string ImapPassword,
    string? Mailbox,
    string? SearchFrom,
    string? SearchSubject);
