namespace DDCRM.Core.Api.AccountsManager;

public interface IAccountsManagerClient
{
    Task<IReadOnlyList<AccountsManagerWorkerServerDefinition>> ListWorkerServersAsync(
        CancellationToken cancellationToken);

    Task<AccountsManagerWorkerServerDefinition> UpsertWorkerServerAsync(
        string serverId,
        AccountsManagerWorkerServerUpsertInput input,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AccountsManagerAccountTypeDefinition>> ListAccountTypesAsync(
        CancellationToken cancellationToken);

    Task<AccountsManagerAccountTypeDefinition> UpsertAccountTypeAsync(
        string accountTypeId,
        AccountsManagerAccountTypeUpsertInput input,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task CreateLifecycleAsync(
        Guid projectId,
        Guid accountId,
        string platform,
        IDictionary<string, object?> proxyConfig,
        AccountsManagerMarketplaceAuth? marketplaceAuth,
        AccountsManagerMailConfig? mailConfig,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task UpdateLifecycleAsync(
        Guid accountId,
        IDictionary<string, object?> proxyConfig,
        AccountsManagerMailConfig? mailConfig,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task DeleteLifecycleAsync(
        Guid accountId,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed record AccountsManagerAccountTypeField(
    string Key,
    string Label,
    string InputType,
    bool Required,
    bool Secret,
    string? Placeholder,
    string? DefaultValue);

public sealed record AccountsManagerAccountTypeDefinition(
    string AccountTypeId,
    string Platform,
    string DisplayName,
    string? Description,
    string WorkerProfileId,
    bool Enabled,
    int SortOrder,
    IReadOnlyList<AccountsManagerAccountTypeField> FormFields,
    AccountsManagerAccountTypeRuntime Runtime);

public sealed record AccountsManagerAccountTypeRuntime(
    bool AutospawnEnabled,
    string WorkerImage,
    string WorkerPathPrefix,
    string HealthPath,
    int ContainerPort,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    IReadOnlyList<string>? WorkerCommand);

public sealed record AccountsManagerAccountTypeUpsertInput(
    string? Platform,
    string? DisplayName,
    string? Description,
    string? WorkerProfileId,
    bool? Enabled,
    int? SortOrder,
    IReadOnlyList<AccountsManagerAccountTypeField>? FormFields,
    AccountsManagerAccountTypeRuntime? Runtime);

public sealed record AccountsManagerWorkerServerDefinition(
    string ServerId,
    string BaseUrlTemplate,
    string Status,
    string Health,
    int Capacity,
    int CurrentLoad,
    string? DockerHost,
    string? DockerNetwork,
    DateTimeOffset? LastHeartbeatAtUtc,
    AccountsManagerWorkerServerRegistrySummary Registry,
    IReadOnlyDictionary<string, object?> Metadata);

public sealed record AccountsManagerWorkerServerUpsertInput(
    string? BaseUrlTemplate,
    string? Status,
    int? Capacity,
    int? CurrentLoad,
    string? Health,
    string? DockerHost,
    string? DockerNetwork,
    AccountsManagerWorkerServerRegistryUpsertInput? Registry,
    IReadOnlyDictionary<string, object?>? Metadata);

public sealed record AccountsManagerWorkerServerRegistrySummary(
    bool Enabled,
    string Host,
    string? Username,
    bool HasToken,
    DateTimeOffset? TokenUpdatedAtUtc);

public sealed record AccountsManagerWorkerServerRegistryUpsertInput(
    bool? Enabled,
    string? Host,
    string? Username,
    string? Token,
    bool? ClearToken);

public sealed record AccountsManagerMarketplaceAuth(
    string Scheme,
    IReadOnlyDictionary<string, string> Credentials);

public sealed record AccountsManagerMailConfig(
    bool Enabled,
    string ImapHost,
    int ImapPort,
    string ImapSecurity,
    string ImapUsername,
    string ImapPassword,
    string? Mailbox,
    string? SearchFrom,
    string? SearchSubject);
