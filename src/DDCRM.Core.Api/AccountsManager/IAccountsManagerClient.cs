namespace DDCRM.Core.Api.AccountsManager;

public interface IAccountsManagerClient
{
    Task<IReadOnlyList<AccountsManagerAccountTypeDefinition>> ListAccountTypesAsync(
        CancellationToken cancellationToken);

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
    IReadOnlyList<AccountsManagerAccountTypeField> FormFields);
