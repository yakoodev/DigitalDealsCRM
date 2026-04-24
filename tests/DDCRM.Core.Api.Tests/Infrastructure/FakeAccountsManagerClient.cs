using DDCRM.Core.Api.AccountsManager;

namespace DDCRM.Core.Api.Tests.Infrastructure;

public sealed class FakeAccountsManagerClient : IAccountsManagerClient
{
    public List<CreateLifecycleCall> CreateCalls { get; } = [];

    public List<UpdateLifecycleCall> UpdateCalls { get; } = [];

    public List<DeleteLifecycleCall> DeleteCalls { get; } = [];

    public Task CreateLifecycleAsync(
        Guid projectId,
        Guid accountId,
        string platform,
        IDictionary<string, object?> proxyConfig,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        CreateCalls.Add(new CreateLifecycleCall(projectId, accountId, platform, idempotencyKey, new Dictionary<string, object?>(proxyConfig, StringComparer.Ordinal)));
        return Task.CompletedTask;
    }

    public Task UpdateLifecycleAsync(
        Guid accountId,
        IDictionary<string, object?> proxyConfig,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        UpdateCalls.Add(new UpdateLifecycleCall(accountId, idempotencyKey, new Dictionary<string, object?>(proxyConfig, StringComparer.Ordinal)));
        return Task.CompletedTask;
    }

    public Task DeleteLifecycleAsync(Guid accountId, string idempotencyKey, CancellationToken cancellationToken)
    {
        DeleteCalls.Add(new DeleteLifecycleCall(accountId, idempotencyKey));
        return Task.CompletedTask;
    }

    public void Reset()
    {
        CreateCalls.Clear();
        UpdateCalls.Clear();
        DeleteCalls.Clear();
    }
}

public sealed record CreateLifecycleCall(
    Guid ProjectId,
    Guid AccountId,
    string Platform,
    string IdempotencyKey,
    IReadOnlyDictionary<string, object?> ProxyConfig);

public sealed record UpdateLifecycleCall(
    Guid AccountId,
    string IdempotencyKey,
    IReadOnlyDictionary<string, object?> ProxyConfig);

public sealed record DeleteLifecycleCall(
    Guid AccountId,
    string IdempotencyKey);
