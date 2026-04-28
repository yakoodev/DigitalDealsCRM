using DDCRM.Core.Api.AccountsManager;

namespace DDCRM.Core.Api.Tests.Infrastructure;

public sealed class FakeAccountsManagerClient : IAccountsManagerClient
{
    public List<UpsertWorkerServerCall> UpsertWorkerServerCalls { get; } = [];

    public List<UpsertAccountTypeCall> UpsertAccountTypeCalls { get; } = [];

    public List<CreateLifecycleCall> CreateCalls { get; } = [];

    public List<UpdateLifecycleCall> UpdateCalls { get; } = [];

    public List<DeleteLifecycleCall> DeleteCalls { get; } = [];

    public List<AccountsManagerAccountTypeDefinition> AccountTypes { get; } = CreateDefaultAccountTypes();

    public List<AccountsManagerWorkerServerDefinition> WorkerServers { get; } = [];

    public Task<IReadOnlyList<AccountsManagerWorkerServerDefinition>> ListWorkerServersAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<AccountsManagerWorkerServerDefinition>>(WorkerServers.ToList());
    }

    public Task<AccountsManagerWorkerServerDefinition> UpsertWorkerServerAsync(
        string serverId,
        AccountsManagerWorkerServerUpsertInput input,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var existingIndex = WorkerServers.FindIndex(x => string.Equals(x.ServerId, serverId, StringComparison.Ordinal));
        var existing = existingIndex >= 0 ? WorkerServers[existingIndex] : null;
        var now = DateTimeOffset.UtcNow;
        var existingRegistry = existing?.Registry ?? new AccountsManagerWorkerServerRegistrySummary(
            false,
            "ghcr.io",
            null,
            false,
            null);
        var registryInput = input.Registry;
        var registryHasToken = existingRegistry.HasToken;
        var registryTokenUpdatedAt = existingRegistry.TokenUpdatedAtUtc;
        if (registryInput?.ClearToken == true)
        {
            registryHasToken = false;
            registryTokenUpdatedAt = now;
        }
        else if (!string.IsNullOrWhiteSpace(registryInput?.Token))
        {
            registryHasToken = true;
            registryTokenUpdatedAt = now;
        }

        var merged = new AccountsManagerWorkerServerDefinition(
            serverId,
            input.BaseUrlTemplate ?? existing?.BaseUrlTemplate ?? "http://{workerId}:{workerPort}",
            input.Status ?? existing?.Status ?? "active",
            input.Health ?? existing?.Health ?? "healthy",
            input.Capacity ?? existing?.Capacity ?? 0,
            input.CurrentLoad ?? existing?.CurrentLoad ?? 0,
            input.DockerHost ?? existing?.DockerHost,
            input.DockerNetwork ?? existing?.DockerNetwork,
            existing?.LastHeartbeatAtUtc,
            new AccountsManagerWorkerServerRegistrySummary(
                registryInput?.Enabled ?? existingRegistry.Enabled,
                registryInput?.Host ?? existingRegistry.Host,
                registryInput?.Username ?? existingRegistry.Username,
                registryHasToken,
                registryTokenUpdatedAt),
            input.Metadata is null
                ? existing?.Metadata ?? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(input.Metadata));

        if (existingIndex >= 0)
        {
            WorkerServers[existingIndex] = merged;
        }
        else
        {
            WorkerServers.Add(merged);
        }

        UpsertWorkerServerCalls.Add(new UpsertWorkerServerCall(serverId, idempotencyKey, input));
        return Task.FromResult(merged);
    }

    public Task<IReadOnlyList<AccountsManagerAccountTypeDefinition>> ListAccountTypesAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<AccountsManagerAccountTypeDefinition>>(AccountTypes.ToList());
    }

    public Task<AccountsManagerAccountTypeDefinition> UpsertAccountTypeAsync(
        string accountTypeId,
        AccountsManagerAccountTypeUpsertInput input,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var existingIndex = AccountTypes.FindIndex(x => string.Equals(x.AccountTypeId, accountTypeId, StringComparison.Ordinal));
        var existing = existingIndex >= 0 ? AccountTypes[existingIndex] : null;
        var merged = new AccountsManagerAccountTypeDefinition(
            accountTypeId,
            input.Platform ?? existing?.Platform ?? "funpay",
            input.DisplayName ?? existing?.DisplayName ?? accountTypeId,
            input.Description ?? existing?.Description,
            input.WorkerProfileId ?? existing?.WorkerProfileId ?? "test-worker",
            input.Enabled ?? existing?.Enabled ?? true,
            input.SortOrder ?? existing?.SortOrder ?? 100,
            input.FormFields ?? existing?.FormFields ?? [],
            input.Runtime ?? existing?.Runtime ?? new AccountsManagerAccountTypeRuntime(
                true,
                "ddcrm/worker-api:local",
                "/internal/v2/worker",
                "/health",
                8080,
                new Dictionary<string, string>()));

        if (existingIndex >= 0)
        {
            AccountTypes[existingIndex] = merged;
        }
        else
        {
            AccountTypes.Add(merged);
        }

        UpsertAccountTypeCalls.Add(new UpsertAccountTypeCall(accountTypeId, idempotencyKey));
        return Task.FromResult(merged);
    }

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
        UpsertWorkerServerCalls.Clear();
        UpsertAccountTypeCalls.Clear();
        WorkerServers.Clear();
        AccountTypes.Clear();
        AccountTypes.AddRange(CreateDefaultAccountTypes());
        CreateCalls.Clear();
        UpdateCalls.Clear();
        DeleteCalls.Clear();
    }

    private static List<AccountsManagerAccountTypeDefinition> CreateDefaultAccountTypes()
    {
        return
        [
            new(
                "test-worker.funpay",
                "funpay",
                "Тестовый worker: FunPay",
                "Тестовый профиль для аккаунта FunPay.",
                "test-worker",
                true,
                10,
                [
                    new("displayName", "Название аккаунта", "text", true, false, "Например, FunPay Test Account", "FunPay Test Account"),
                    new("proxyHost", "Proxy host", "text", true, false, "45.88.208.237", null),
                    new("proxyPort", "Proxy port", "number", true, false, "1508", "1508"),
                    new("proxyLogin", "Proxy login", "text", true, false, "user305829", null),
                    new("proxyPassword", "Proxy password", "password", true, true, "Введите пароль", null),
                ],
                new AccountsManagerAccountTypeRuntime(
                    true,
                    "ddcrm/worker-api:local",
                    "/internal/v2/worker",
                    "/health",
                    8080,
                    new Dictionary<string, string>())),
            new(
                "test-worker.playerok",
                "playerok",
                "Тестовый worker: Playerok",
                "Тестовый профиль для аккаунта Playerok.",
                "test-worker",
                true,
                20,
                [
                    new("displayName", "Название аккаунта", "text", true, false, "Например, Playerok Test Account", "Playerok Test Account"),
                    new("proxyHost", "Proxy host", "text", true, false, "45.88.208.237", null),
                    new("proxyPort", "Proxy port", "number", true, false, "1508", "1508"),
                    new("proxyLogin", "Proxy login", "text", true, false, "user305829", null),
                    new("proxyPassword", "Proxy password", "password", true, true, "Введите пароль", null),
                ],
                new AccountsManagerAccountTypeRuntime(
                    true,
                    "ddcrm/worker-api:local",
                    "/internal/v2/worker",
                    "/health",
                    8080,
                    new Dictionary<string, string>())),
            new(
                "test-worker.ggsell",
                "ggsell",
                "Тестовый worker: GGSell",
                "Тестовый профиль для аккаунта GGSell.",
                "test-worker",
                true,
                30,
                [
                    new("displayName", "Название аккаунта", "text", true, false, "Например, GGSell Test Account", "GGSell Test Account"),
                    new("proxyHost", "Proxy host", "text", true, false, "45.88.208.237", null),
                    new("proxyPort", "Proxy port", "number", true, false, "1508", "1508"),
                    new("proxyLogin", "Proxy login", "text", true, false, "user305829", null),
                    new("proxyPassword", "Proxy password", "password", true, true, "Введите пароль", null),
                ],
                new AccountsManagerAccountTypeRuntime(
                    true,
                    "ddcrm/worker-api:local",
                    "/internal/v2/worker",
                    "/health",
                    8080,
                    new Dictionary<string, string>())),
            new(
                "test-worker.platimarket",
                "platimarket",
                "Тестовый worker: PlatiMarket",
                "Тестовый профиль для аккаунта PlatiMarket.",
                "test-worker",
                true,
                40,
                [
                    new("displayName", "Название аккаунта", "text", true, false, "Например, PlatiMarket Test Account", "PlatiMarket Test Account"),
                    new("proxyHost", "Proxy host", "text", true, false, "45.88.208.237", null),
                    new("proxyPort", "Proxy port", "number", true, false, "1508", "1508"),
                    new("proxyLogin", "Proxy login", "text", true, false, "user305829", null),
                    new("proxyPassword", "Proxy password", "password", true, true, "Введите пароль", null),
                ],
                new AccountsManagerAccountTypeRuntime(
                    true,
                    "ddcrm/worker-api:local",
                    "/internal/v2/worker",
                    "/health",
                    8080,
                    new Dictionary<string, string>())),
        ];
    }
}

public sealed record UpsertWorkerServerCall(
    string ServerId,
    string IdempotencyKey,
    AccountsManagerWorkerServerUpsertInput Input);

public sealed record UpsertAccountTypeCall(
    string AccountTypeId,
    string IdempotencyKey);

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
