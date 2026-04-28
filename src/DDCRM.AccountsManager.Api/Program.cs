using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DDCRM.AccountsManager.Api.RouteRegistry;
using DDCRM.AccountsManager.Api.Worker;
using DDCRM.AccountsManager.Persistence;
using DDCRM.AccountsManager.Persistence.Entities;
using DDCRM.Shared.Auth;
using DDCRM.Shared.Errors;
using DDCRM.Shared.Extensions;
using DDCRM.Shared.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AccountsManagerDbContext>((serviceProvider, options) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var useInMemoryDb = configuration.GetValue("TEST_USE_INMEMORY_DB", false);

    if (useInMemoryDb)
    {
        options.UseInMemoryDatabase(configuration["TEST_INMEMORY_DB_NAME"] ?? "ddcrm-accounts-manager-tests");
        return;
    }

    options.UseNpgsql(
        configuration.GetConnectionString("AccountsManagerDb")
        ?? configuration["ACCOUNTS_MANAGER_DB_CONNECTION"]
        ?? "Host=localhost;Port=5432;Database=ddcrm_accounts_manager;Username=postgres;Password=postgres");
});

builder.Services.AddScoped<IdempotencyExecutor>();

builder.Services.AddServiceTokenAuth(options =>
{
    options.Enabled = builder.Configuration.GetValue("INTERNAL_API_SERVICE_AUTH_ENABLED", true);
    options.AcceptedTokens = builder.Configuration.GetCommaSeparatedValues("INTERNAL_API_SERVICE_AUTH_ACCEPTED_TOKENS");
    options.ForbiddenTokens = builder.Configuration.GetCommaSeparatedValues("WORKER_API_SERVICE_AUTH_ACCEPTED_TOKENS");
});

builder.Services.Configure<RouteRegistryClientOptions>(options =>
{
    options.Enabled = builder.Configuration.GetValue("ROUTE_REGISTRY_CLIENT_ENABLED", false);
    options.BaseUrl = builder.Configuration["ROUTE_REGISTRY_CLIENT_BASE_URL"];
    options.ServiceToken = builder.Configuration["INTERNAL_API_SERVICE_AUTH_CLIENT_TOKEN"];
});
builder.Services.Configure<WorkerControlClientOptions>(options =>
{
    options.Enabled = builder.Configuration.GetValue("WORKER_CONTROL_CLIENT_ENABLED", true);
    options.BaseUrlTemplate = builder.Configuration["WORKER_CONTROL_CLIENT_BASE_URL_TEMPLATE"] ?? options.BaseUrlTemplate;
    options.PathPrefix = builder.Configuration["WORKER_CONTROL_CLIENT_PATH_PREFIX"] ?? options.PathPrefix;
    options.ServiceToken = builder.Configuration["WORKER_API_SERVICE_AUTH_CLIENT_TOKEN"];
});
builder.Services.Configure<AccountManagerAutospawnOptions>(options =>
{
    options.Enabled = builder.Configuration.GetValue("ACCOUNT_MANAGER_AUTOSPAWN_ENABLED", false);
    options.DockerEndpoint = builder.Configuration["ACCOUNT_MANAGER_AUTOSPAWN_DOCKER_ENDPOINT"] ?? options.DockerEndpoint;
    options.DockerNetwork = builder.Configuration["ACCOUNT_MANAGER_AUTOSPAWN_DOCKER_NETWORK"] ?? options.DockerNetwork;
    options.WorkerInternalPort = builder.Configuration.GetValue("ACCOUNT_MANAGER_AUTOSPAWN_WORKER_INTERNAL_PORT", options.WorkerInternalPort);
    options.HealthTimeoutSeconds = builder.Configuration.GetValue("ACCOUNT_MANAGER_AUTOSPAWN_HEALTH_TIMEOUT_SECONDS", options.HealthTimeoutSeconds);
    options.HealthPollIntervalMilliseconds = builder.Configuration.GetValue("ACCOUNT_MANAGER_AUTOSPAWN_HEALTH_POLL_INTERVAL_MS", options.HealthPollIntervalMilliseconds);
    options.FallbackWorkerId = builder.Configuration["ACCOUNT_MANAGER_AUTOSPAWN_FALLBACK_WORKER_ID"] ?? options.FallbackWorkerId;
    options.WorkerApiServiceToken = builder.Configuration["WORKER_API_SERVICE_AUTH_CLIENT_TOKEN"];
});

builder.Services.AddHttpClient<IRouteRegistryClient, RouteRegistryHttpClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<RouteRegistryClientOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(options.BaseUrl))
    {
        client.BaseAddress = new Uri(options.BaseUrl);
    }
});
builder.Services.AddHttpClient<IWorkerControlClient, WorkerControlHttpClient>();
builder.Services.AddHttpClient("docker-autospawn-health", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<IDockerWorkerRuntimeClient, DockerWorkerRuntimeClient>();
var registrySecretEncryptionKey = ResolveRegistrySecretsEncryptionKey(
    builder.Configuration["ACCOUNT_MANAGER_AUTOSPAWN_REGISTRY_SECRET_ENCRYPTION_KEY"]);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AccountsManagerDbContext>();
    if (db.Database.IsRelational())
    {
        db.Database.Migrate();
    }
    else
    {
        db.Database.EnsureCreated();
    }

    await EnsureDefaultAccountTypesAsync(db);
}

app.UseDdcrmCommonPipeline();
app.UseServiceTokenAuth();

app.MapGet("/health", (HttpContext httpContext) =>
    Results.Ok(new
    {
        requestId = httpContext.GetOrCreateRequestId(),
        status = "ok",
    }));

var workerServers = app.MapGroup("/internal/v1/worker-servers");

workerServers.MapGet(string.Empty, async (
    HttpContext httpContext,
    AccountsManagerDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entities = await dbContext.WorkerServers
        .AsNoTracking()
        .OrderBy(x => x.ServerId)
        .ToListAsync(cancellationToken);
    var items = entities.Select(ToWorkerServerDto).ToList();

    return Results.Ok(new WorkerServerListResponse(httpContext.GetOrCreateRequestId(), items));
});

app.MapGet("/internal/v1/account-types", async (
    HttpContext httpContext,
    AccountsManagerDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var entities = await dbContext.AccountTypes
        .AsNoTracking()
        .Where(x => x.Enabled)
        .OrderBy(x => x.SortOrder)
        .ThenBy(x => x.DisplayName)
        .ToListAsync(cancellationToken);

    var items = entities.Select(ToAccountTypeDto).ToList();

    return Results.Ok(new AccountTypeListResponse(httpContext.GetOrCreateRequestId(), items));
});

app.MapPut("/internal/v1/account-types/{accountTypeId}", async (
    HttpContext httpContext,
    string accountTypeId,
    AccountTypeUpsertRequest request,
    AccountsManagerDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    var normalizedAccountTypeId = NormalizeAccountTypeId(accountTypeId);

    return await idempotency.ExecuteAsync(
        dbContext,
        $"accounts-manager:account-type:upsert:{normalizedAccountTypeId}",
        idempotencyKey,
        async ct =>
        {
            var existing = await dbContext.AccountTypes.SingleOrDefaultAsync(
                x => x.AccountTypeId == normalizedAccountTypeId,
                ct);
            var now = DateTimeOffset.UtcNow;

            var platform = NormalizePlatform(request.Platform, existing);
            var enabled = request.Enabled ?? existing?.Enabled ?? true;
            if (enabled)
            {
                var otherEnabled = await dbContext.AccountTypes
                    .AsNoTracking()
                    .Where(x => x.AccountTypeId != normalizedAccountTypeId)
                    .AnyAsync(
                        x => x.Enabled
                             && x.Platform == platform,
                        ct);
                if (otherEnabled)
                {
                    throw new ApiErrorException(
                        StatusCodes.Status409Conflict,
                        ApiErrorCodes.Conflict,
                        $"Для платформы `{platform}` уже существует активный account-type. Допустим только один активный профиль на платформу.");
                }
            }

            var displayName = NormalizeAccountTypeDisplayName(request.DisplayName, existing);
            var workerProfileId = NormalizeWorkerProfileId(request.WorkerProfileId, existing);
            var sortOrder = request.SortOrder ?? existing?.SortOrder ?? 100;
            var description = request.Description?.Trim() ?? existing?.Description;
            var formFields = NormalizeFormFields(request.FormFields, existing);
            var runtime = NormalizeRuntimeConfig(request.Runtime, existing, platform);

            if (existing is null)
            {
                existing = new AccountTypeEntity
                {
                    AccountTypeId = normalizedAccountTypeId,
                    Platform = platform,
                    DisplayName = displayName,
                    Description = description,
                    WorkerProfileId = workerProfileId,
                    Enabled = enabled,
                    SortOrder = sortOrder,
                    FormFieldsJson = SerializeFormFields(formFields),
                    RuntimeConfigJson = SerializeRuntimeConfig(runtime),
                    UpdatedAtUtc = now,
                };
                dbContext.AccountTypes.Add(existing);
            }
            else
            {
                existing.Platform = platform;
                existing.DisplayName = displayName;
                existing.Description = description;
                existing.WorkerProfileId = workerProfileId;
                existing.Enabled = enabled;
                existing.SortOrder = sortOrder;
                existing.FormFieldsJson = SerializeFormFields(formFields);
                existing.RuntimeConfigJson = SerializeRuntimeConfig(runtime);
                existing.UpdatedAtUtc = now;
            }

            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AccountTypeResponse(httpContext.GetOrCreateRequestId(), ToAccountTypeDto(existing)));
        },
        cancellationToken);
});

workerServers.MapPut("/{serverId}", async (
    HttpContext httpContext,
    string serverId,
    WorkerServerUpsertRequest request,
    AccountsManagerDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    var normalizedServerId = NormalizeServerId(serverId);

    return await idempotency.ExecuteAsync(
        dbContext,
        $"accounts-manager:worker-server:upsert:{normalizedServerId}",
        idempotencyKey,
        async ct =>
        {
            var existing = await dbContext.WorkerServers.SingleOrDefaultAsync(
                x => x.ServerId == normalizedServerId,
                ct);

            var baseUrlTemplate = NormalizeBaseUrlTemplate(request.BaseUrlTemplate, existing);
            var status = NormalizeWorkerServerStatus(request.Status, existing);
            var health = NormalizeWorkerServerHealth(request.Health, existing);
            var capacity = NormalizeCapacity(request.Capacity, existing);
            var currentLoad = NormalizeCurrentLoad(request.CurrentLoad, existing, capacity);
            var dockerHost = NormalizeDockerHost(request.DockerHost, existing);
            var dockerNetwork = NormalizeDockerNetwork(request.DockerNetwork, existing);
            var metadata = NormalizeMetadata(request.Metadata, existing);
            var now = DateTimeOffset.UtcNow;
            var registry = NormalizeWorkerServerRegistry(
                request.Registry,
                existing,
                registrySecretEncryptionKey,
                now);

            if (existing is null)
            {
                existing = new WorkerServerEntity
                {
                    ServerId = normalizedServerId,
                    BaseUrlTemplate = baseUrlTemplate,
                    Status = status,
                    Health = health,
                    Capacity = capacity,
                    CurrentLoad = currentLoad,
                    DockerHost = dockerHost,
                    DockerNetwork = dockerNetwork,
                    RegistryEnabled = registry.Enabled,
                    RegistryHost = registry.Host,
                    RegistryUsername = registry.Username,
                    RegistryTokenEncrypted = registry.TokenEncrypted,
                    RegistryTokenUpdatedAtUtc = registry.TokenUpdatedAtUtc,
                    MetadataJson = SerializeMetadata(metadata),
                    UpdatedAtUtc = now,
                };
                dbContext.WorkerServers.Add(existing);
            }
            else
            {
                existing.BaseUrlTemplate = baseUrlTemplate;
                existing.Status = status;
                existing.Health = health;
                existing.Capacity = capacity;
                existing.CurrentLoad = currentLoad;
                existing.DockerHost = dockerHost;
                existing.DockerNetwork = dockerNetwork;
                existing.RegistryEnabled = registry.Enabled;
                existing.RegistryHost = registry.Host;
                existing.RegistryUsername = registry.Username;
                existing.RegistryTokenEncrypted = registry.TokenEncrypted;
                existing.RegistryTokenUpdatedAtUtc = registry.TokenUpdatedAtUtc;
                existing.MetadataJson = SerializeMetadata(metadata);
                existing.UpdatedAtUtc = now;
            }

            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new WorkerServerResponse(httpContext.GetOrCreateRequestId(), ToWorkerServerDto(existing)));
        },
        cancellationToken);
});

workerServers.MapPost("/{serverId}/heartbeat", async (
    HttpContext httpContext,
    string serverId,
    WorkerServerHeartbeatRequest request,
    AccountsManagerDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var normalizedServerId = NormalizeServerId(serverId);
    var existing = await dbContext.WorkerServers.SingleOrDefaultAsync(
        x => x.ServerId == normalizedServerId,
        cancellationToken);

    if (existing is null)
    {
        throw new ApiErrorException(
            StatusCodes.Status404NotFound,
            ApiErrorCodes.NotFound,
            "Worker server не найден.");
    }

    var capacity = request.Capacity ?? existing.Capacity;
    if (capacity < 0)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "capacity не может быть отрицательным.");
    }

    var currentLoad = request.CurrentLoad ?? existing.CurrentLoad;
    if (currentLoad < 0)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "currentLoad не может быть отрицательным.");
    }

    if (capacity > 0 && currentLoad > capacity)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "currentLoad не может превышать capacity.");
    }

    if (request.Status is not null)
    {
        existing.Status = NormalizeWorkerServerStatus(request.Status, existing);
    }

    if (request.Health is not null)
    {
        existing.Health = NormalizeWorkerServerHealth(request.Health, existing);
    }

    if (request.Metadata is not null)
    {
        existing.MetadataJson = SerializeMetadata(request.Metadata);
    }

    if (request.DockerHost is not null)
    {
        existing.DockerHost = NormalizeDockerHost(request.DockerHost, existing);
    }

    if (request.DockerNetwork is not null)
    {
        existing.DockerNetwork = NormalizeDockerNetwork(request.DockerNetwork, existing);
    }

    existing.Capacity = capacity;
    existing.CurrentLoad = currentLoad;
    existing.LastHeartbeatAtUtc = DateTimeOffset.UtcNow;
    existing.UpdatedAtUtc = DateTimeOffset.UtcNow;

    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
});

var lifecycle = app.MapGroup("/internal/v1/lifecycle");

lifecycle.MapPost("/create", async (
    HttpContext httpContext,
    LifecycleCreateRequest request,
    AccountsManagerDbContext dbContext,
    IRouteRegistryClient routeRegistryClient,
    IWorkerControlClient workerControlClient,
    IDockerWorkerRuntimeClient dockerWorkerRuntimeClient,
    IOptions<AccountManagerAutospawnOptions> autospawnOptions,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    if (string.IsNullOrWhiteSpace(request.Platform))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "platform обязателен для lifecycle create.");
    }
    var normalizedPlatform = request.Platform.Trim().ToLowerInvariant();

    if (request.ProxyConfig is null || request.ProxyConfig.Count == 0)
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "proxyConfig обязателен для lifecycle create.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"accounts-manager:create:{request.AccountId}",
        idempotencyKey,
        async ct =>
        {
            var existing = await dbContext.WorkerPlacements.SingleOrDefaultAsync(x => x.AccountId == request.AccountId, ct);
            if (existing is not null)
            {
                throw new ApiErrorException(StatusCodes.Status409Conflict, ApiErrorCodes.Conflict, "Worker placement уже существует.");
            }

            var availableServers = await dbContext.WorkerServers.ToListAsync(ct);
            var targetServer = ResolveCreateTargetServer(availableServers);
            var accountType = await ResolveActiveAccountTypeForPlatformAsync(
                dbContext,
                normalizedPlatform,
                ct);
            var runtimeConfig = ResolveRuntimeConfig(accountType, normalizedPlatform);

            var spawnEnabled = dockerWorkerRuntimeClient.Enabled && runtimeConfig.AutospawnEnabled;
            var fallbackWorkerId = NormalizeFallbackWorkerId(autospawnOptions.Value.FallbackWorkerId);
            var initialWorkerId = spawnEnabled
                ? BuildSpawnWorkerId(request.AccountId)
                : fallbackWorkerId;
            var initialPodId = spawnEnabled
                ? $"pod-{Guid.NewGuid():N}"[..16]
                : $"pod-{request.AccountId:N}"[..16];
            var workerPort = ResolveWorkerPort(runtimeConfig, autospawnOptions.Value.WorkerInternalPort);
            var workerId = initialWorkerId;
            var podId = initialPodId;
            if (spawnEnabled)
            {
                var spawnResult = await dockerWorkerRuntimeClient.SpawnWorkerAsync(
                    new DockerSpawnRequest(
                        initialWorkerId,
                        normalizedPlatform,
                        runtimeConfig.WorkerImage,
                        workerPort,
                        targetServer.Server?.DockerNetwork,
                        targetServer.Server?.DockerHost,
                        BuildWorkerSpawnEnvironment(runtimeConfig, normalizedPlatform, request.AccountId),
                        runtimeConfig.HealthPath,
                        ResolveRegistryAuth(targetServer.Server, registrySecretEncryptionKey)),
                    ct);
                workerId = spawnResult.WorkerId;
                podId = spawnResult.PodId;
            }
            var workerControlBaseUrlTemplate = ResolveWorkerControlBaseUrlTemplate(
                workerId,
                targetServer.Server,
                workerPort,
                targetServer.BaseUrlTemplate);

            var routeVersion = 1;
            var workerBinding = new WorkerBindingDto(targetServer.ServerId, workerId, podId);

            await routeRegistryClient.UpsertAsync(
                request.AccountId,
                new RouteUpsertRequestDto(
                    request.ProjectId,
                    BuildRouteKey(request.AccountId),
                    workerBinding,
                    routeVersion),
                idempotencyKey,
                ct);

            try
            {
                await workerControlClient.ApplyProxyCredentialsAsync(
                    workerBinding,
                    request.AccountId,
                    request.ProxyConfig,
                    idempotencyKey,
                    workerControlBaseUrlTemplate,
                    ct);
            }
            catch
            {
                if (spawnEnabled)
                {
                    await dockerWorkerRuntimeClient.RemoveWorkerAsync(
                        new DockerRemoveRequest(workerId, targetServer.Server?.DockerHost),
                        ct);
                }

                throw;
            }

            var entity = new WorkerPlacementEntity
            {
                AccountId = request.AccountId,
                ProjectId = request.ProjectId,
                Platform = normalizedPlatform,
                WorkerId = workerId,
                ServerId = targetServer.ServerId,
                PodId = podId,
                LifecycleStatus = "active",
                ProxyConfigured = true,
                RouteVersion = routeVersion,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };

            dbContext.WorkerPlacements.Add(entity);
            IncrementServerLoad(targetServer.Server);

            dbContext.LifecycleAudits.Add(CreateAudit(entity, "create", "accounts-manager", $"worker placement created on {targetServer.ServerId}"));
            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(StatusCodes.Status202Accepted, new AckResponse(httpContext.GetOrCreateRequestId(), "accepted"));
        },
        cancellationToken);
});

lifecycle.MapPost("/update", async (
    HttpContext httpContext,
    LifecycleUpdateRequest request,
    AccountsManagerDbContext dbContext,
    IWorkerControlClient workerControlClient,
    IOptions<AccountManagerAutospawnOptions> autospawnOptions,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    if (request.ProxyConfig is null || request.ProxyConfig.Count == 0)
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "proxyConfig обязателен для lifecycle update.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"accounts-manager:update:{request.AccountId}",
        idempotencyKey,
        async ct =>
        {
            var existing = await dbContext.WorkerPlacements.SingleOrDefaultAsync(x => x.AccountId == request.AccountId, ct);
            if (existing is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Worker placement не найден.");
            }

            var workerServer = await dbContext.WorkerServers
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.ServerId == existing.ServerId, ct);
            var accountType = await ResolveActiveAccountTypeForPlatformAsync(
                dbContext,
                existing.Platform,
                ct);
            var runtimeConfig = ResolveRuntimeConfig(accountType, existing.Platform);
            var workerPort = ResolveWorkerPort(runtimeConfig, autospawnOptions.Value.WorkerInternalPort);
            var workerControlBaseUrlTemplate = ResolveWorkerControlBaseUrlTemplate(
                existing.WorkerId,
                workerServer,
                workerPort,
                workerServer?.BaseUrlTemplate);

            await workerControlClient.ApplyProxyCredentialsAsync(
                new WorkerBindingDto(existing.ServerId, existing.WorkerId, existing.PodId),
                request.AccountId,
                request.ProxyConfig!,
                idempotencyKey,
                workerControlBaseUrlTemplate,
                ct);

            existing.ProxyConfigured = true;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;

            dbContext.LifecycleAudits.Add(CreateAudit(existing, "update", "accounts-manager", "worker placement updated"));
            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(StatusCodes.Status202Accepted, new AckResponse(httpContext.GetOrCreateRequestId(), "accepted"));
        },
        cancellationToken);
});

lifecycle.MapPost("/delete", async (
    HttpContext httpContext,
    LifecycleDeleteRequest request,
    AccountsManagerDbContext dbContext,
    IRouteRegistryClient routeRegistryClient,
    IDockerWorkerRuntimeClient dockerWorkerRuntimeClient,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    return await idempotency.ExecuteAsync(
        dbContext,
        $"accounts-manager:delete:{request.AccountId}",
        idempotencyKey,
        async ct =>
        {
            var existing = await dbContext.WorkerPlacements.SingleOrDefaultAsync(x => x.AccountId == request.AccountId, ct);
            if (existing is null)
            {
                await routeRegistryClient.DeleteAsync(request.AccountId, idempotencyKey, ct);
                return new IdempotentExecutionResult(StatusCodes.Status202Accepted, new AckResponse(httpContext.GetOrCreateRequestId(), "accepted"));
            }

            var sourceServer = await dbContext.WorkerServers.SingleOrDefaultAsync(x => x.ServerId == existing.ServerId, ct);
            if (dockerWorkerRuntimeClient.Enabled && IsManagedWorker(existing.WorkerId))
            {
                await dockerWorkerRuntimeClient.RemoveWorkerAsync(
                    new DockerRemoveRequest(existing.WorkerId, sourceServer?.DockerHost),
                    ct);
            }

            dbContext.LifecycleAudits.Add(CreateAudit(existing, "delete", "accounts-manager", "worker placement deleted"));
            dbContext.WorkerPlacements.Remove(existing);
            DecrementServerLoad(sourceServer);
            await dbContext.SaveChangesAsync(ct);

            await routeRegistryClient.DeleteAsync(request.AccountId, idempotencyKey, ct);

            return new IdempotentExecutionResult(StatusCodes.Status202Accepted, new AckResponse(httpContext.GetOrCreateRequestId(), "accepted"));
        },
        cancellationToken);
});

lifecycle.MapPost("/migrate", async (
    HttpContext httpContext,
    LifecycleMigrateRequest request,
    AccountsManagerDbContext dbContext,
    IRouteRegistryClient routeRegistryClient,
    IDockerWorkerRuntimeClient dockerWorkerRuntimeClient,
    IOptions<AccountManagerAutospawnOptions> autospawnOptions,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    return await idempotency.ExecuteAsync(
        dbContext,
        $"accounts-manager:migrate:{request.AccountId}",
        idempotencyKey,
        async ct =>
        {
            var existing = await dbContext.WorkerPlacements.SingleOrDefaultAsync(x => x.AccountId == request.AccountId, ct);
            if (existing is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Worker placement не найден.");
            }

            var availableServers = await dbContext.WorkerServers.ToListAsync(ct);
            var targetServer = ResolveMigrateTargetServer(availableServers, existing, request.TargetServerId);

            if (targetServer.ServerId.Length > 120)
            {
                throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "targetServerId превышает лимит длины.");
            }

            if (string.Equals(targetServer.ServerId, existing.ServerId, StringComparison.Ordinal))
            {
                return new IdempotentExecutionResult(StatusCodes.Status202Accepted, new AckResponse(httpContext.GetOrCreateRequestId(), "accepted"));
            }

            var accountType = await ResolveActiveAccountTypeForPlatformAsync(
                dbContext,
                existing.Platform,
                ct);
            var runtimeConfig = ResolveRuntimeConfig(accountType, existing.Platform);
            var spawnEnabled = dockerWorkerRuntimeClient.Enabled && runtimeConfig.AutospawnEnabled;
            var workerPort = ResolveWorkerPort(runtimeConfig, autospawnOptions.Value.WorkerInternalPort);

            var previousWorkerId = existing.WorkerId;
            var targetWorkerId = spawnEnabled
                ? BuildSpawnWorkerId(existing.AccountId)
                : previousWorkerId;
            var targetPodId = $"pod-{Guid.NewGuid():N}"[..16];
            if (spawnEnabled)
            {
                var spawnResult = await dockerWorkerRuntimeClient.SpawnWorkerAsync(
                    new DockerSpawnRequest(
                        targetWorkerId,
                        existing.Platform,
                        runtimeConfig.WorkerImage,
                        workerPort,
                        targetServer.Server?.DockerNetwork,
                        targetServer.Server?.DockerHost,
                        BuildWorkerSpawnEnvironment(runtimeConfig, existing.Platform, existing.AccountId),
                        runtimeConfig.HealthPath,
                        ResolveRegistryAuth(targetServer.Server, registrySecretEncryptionKey)),
                    ct);
                targetWorkerId = spawnResult.WorkerId;
                targetPodId = spawnResult.PodId;
            }

            var nextRouteVersion = existing.RouteVersion + 1;
            var previousServerId = existing.ServerId;
            try
            {
                await routeRegistryClient.SwitchAsync(
                    request.AccountId,
                    new RouteSwitchRequestDto(
                        new WorkerBindingDto(targetServer.ServerId, targetWorkerId, targetPodId),
                        nextRouteVersion),
                    idempotencyKey,
                    ct);
            }
            catch
            {
                if (spawnEnabled)
                {
                    await dockerWorkerRuntimeClient.RemoveWorkerAsync(
                        new DockerRemoveRequest(targetWorkerId, targetServer.Server?.DockerHost),
                        ct);
                }

                throw;
            }

            existing.ServerId = targetServer.ServerId;
            existing.WorkerId = targetWorkerId;
            existing.PodId = targetPodId;
            existing.RouteVersion = nextRouteVersion;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;

            DecrementServerLoad(FindServer(availableServers, previousServerId));
            IncrementServerLoad(targetServer.Server);

            dbContext.LifecycleAudits.Add(CreateAudit(existing, "migrate", "accounts-manager", $"migrated to {targetServer.ServerId}"));
            await dbContext.SaveChangesAsync(ct);

            if (spawnEnabled && IsManagedWorker(previousWorkerId))
            {
                var sourceServer = FindServer(availableServers, previousServerId);
                await dockerWorkerRuntimeClient.RemoveWorkerAsync(
                    new DockerRemoveRequest(previousWorkerId, sourceServer?.DockerHost),
                    ct);
            }

            return new IdempotentExecutionResult(StatusCodes.Status202Accepted, new AckResponse(httpContext.GetOrCreateRequestId(), "accepted"));
        },
        cancellationToken);
});

lifecycle.MapPost("/rebalance", async (
    HttpContext httpContext,
    LifecycleRebalanceRequest request,
    AccountsManagerDbContext dbContext,
    IRouteRegistryClient routeRegistryClient,
    IDockerWorkerRuntimeClient dockerWorkerRuntimeClient,
    IOptions<AccountManagerAutospawnOptions> autospawnOptions,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    return await idempotency.ExecuteAsync(
        dbContext,
        "accounts-manager:rebalance",
        idempotencyKey,
        async ct =>
        {
            var placements = await dbContext.WorkerPlacements
                .OrderBy(x => x.UpdatedAtUtc)
                .ToListAsync(ct);
            var servers = await dbContext.WorkerServers.ToListAsync(ct);
            var activeAccountTypes = await dbContext.AccountTypes
                .AsNoTracking()
                .Where(x => x.Enabled)
                .OrderBy(x => x.SortOrder)
                .ToListAsync(ct);
            var accountTypeByPlatform = activeAccountTypes
                .GroupBy(x => x.Platform, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            var maxMoves = request.MaxMoves is null or <= 0
                ? 25
                : Math.Min(request.MaxMoves.Value, 200);

            var migrations = new List<LifecycleRebalanceMove>();
            foreach (var placement in placements)
            {
                if (migrations.Count >= maxMoves)
                {
                    break;
                }

                var targetServer = SelectLeastLoadedServer(servers, placement.ServerId);
                if (targetServer is null)
                {
                    continue;
                }

                var nextRouteVersion = placement.RouteVersion + 1;
                var previousWorkerId = placement.WorkerId;
                var targetWorkerId = previousWorkerId;
                var targetPodId = $"pod-{Guid.NewGuid():N}"[..16];
                var spawnEnabled = false;
                if (accountTypeByPlatform.TryGetValue(placement.Platform, out var platformAccountType))
                {
                    var runtimeConfig = ResolveRuntimeConfig(platformAccountType, placement.Platform);
                    spawnEnabled = dockerWorkerRuntimeClient.Enabled && runtimeConfig.AutospawnEnabled;
                    if (spawnEnabled)
                    {
                        var workerPort = ResolveWorkerPort(runtimeConfig, autospawnOptions.Value.WorkerInternalPort);
                        var spawnResult = await dockerWorkerRuntimeClient.SpawnWorkerAsync(
                            new DockerSpawnRequest(
                                BuildSpawnWorkerId(placement.AccountId),
                                placement.Platform,
                                runtimeConfig.WorkerImage,
                                workerPort,
                                targetServer.DockerNetwork,
                                targetServer.DockerHost,
                                BuildWorkerSpawnEnvironment(runtimeConfig, placement.Platform, placement.AccountId),
                                runtimeConfig.HealthPath,
                                ResolveRegistryAuth(targetServer, registrySecretEncryptionKey)),
                            ct);
                        targetWorkerId = spawnResult.WorkerId;
                        targetPodId = spawnResult.PodId;
                    }
                }

                try
                {
                    await routeRegistryClient.SwitchAsync(
                        placement.AccountId,
                        new RouteSwitchRequestDto(
                            new WorkerBindingDto(targetServer.ServerId, targetWorkerId, targetPodId),
                            nextRouteVersion),
                        idempotencyKey,
                        ct);
                }
                catch
                {
                    if (spawnEnabled)
                    {
                        await dockerWorkerRuntimeClient.RemoveWorkerAsync(
                            new DockerRemoveRequest(targetWorkerId, targetServer.DockerHost),
                            ct);
                    }

                    throw;
                }

                var sourceServerId = placement.ServerId;
                placement.ServerId = targetServer.ServerId;
                placement.WorkerId = targetWorkerId;
                placement.PodId = targetPodId;
                placement.RouteVersion = nextRouteVersion;
                placement.UpdatedAtUtc = DateTimeOffset.UtcNow;

                DecrementServerLoad(FindServer(servers, sourceServerId));
                IncrementServerLoad(targetServer);

                dbContext.LifecycleAudits.Add(CreateAudit(placement, "rebalance", "accounts-manager", $"rebalanced to {targetServer.ServerId}"));
                migrations.Add(new LifecycleRebalanceMove(placement.AccountId, sourceServerId, targetServer.ServerId, nextRouteVersion));

                if (spawnEnabled && IsManagedWorker(previousWorkerId))
                {
                    var sourceServer = FindServer(servers, sourceServerId);
                    await dockerWorkerRuntimeClient.RemoveWorkerAsync(
                        new DockerRemoveRequest(previousWorkerId, sourceServer?.DockerHost),
                        ct);
                }
            }

            if (migrations.Count > 0)
            {
                await dbContext.SaveChangesAsync(ct);
            }

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new LifecycleRebalanceResponse(
                    httpContext.GetOrCreateRequestId(),
                    "completed",
                    placements.Count,
                    migrations.Count,
                    migrations));
        },
        cancellationToken);
});

app.Run();

static async Task EnsureDefaultAccountTypesAsync(AccountsManagerDbContext dbContext)
{
    var now = DateTimeOffset.UtcNow;
    var defaults = CreateDefaultAccountTypes(now);
    var existing = await dbContext.AccountTypes.ToListAsync();
    var existingById = existing.ToDictionary(x => x.AccountTypeId, StringComparer.OrdinalIgnoreCase);

    var hasChanges = false;
    foreach (var accountType in defaults)
    {
        if (!existingById.TryGetValue(accountType.AccountTypeId, out var current))
        {
            dbContext.AccountTypes.Add(accountType);
            hasChanges = true;
            continue;
        }

        if (ApplyAccountTypeDefaults(current, accountType, now))
        {
            hasChanges = true;
        }
    }

    if (hasChanges)
    {
        await dbContext.SaveChangesAsync();
    }
}

static IReadOnlyList<AccountTypeEntity> CreateDefaultAccountTypes(DateTimeOffset now)
{
    return
    [
        CreateDefaultAccountType(
            accountTypeId: "test-worker.funpay",
            platform: "funpay",
            displayName: "Тестовый worker: FunPay",
            description: "Тестовый профиль для аккаунта FunPay.",
            sortOrder: 10,
            defaultDisplayName: "FunPay Test Account",
            now),
        CreateDefaultAccountType(
            accountTypeId: "test-worker.playerok",
            platform: "playerok",
            displayName: "Тестовый worker: Playerok",
            description: "Тестовый профиль для аккаунта Playerok.",
            sortOrder: 20,
            defaultDisplayName: "Playerok Test Account",
            now),
        CreateDefaultAccountType(
            accountTypeId: "test-worker.ggsell",
            platform: "ggsell",
            displayName: "Тестовый worker: GGSell",
            description: "Тестовый профиль для аккаунта GGSell.",
            sortOrder: 30,
            defaultDisplayName: "GGSell Test Account",
            now),
        CreateDefaultAccountType(
            accountTypeId: "test-worker.platimarket",
            platform: "platimarket",
            displayName: "Тестовый worker: PlatiMarket",
            description: "Тестовый профиль для аккаунта PlatiMarket.",
            sortOrder: 40,
            defaultDisplayName: "PlatiMarket Test Account",
            now),
    ];
}

static AccountTypeEntity CreateDefaultAccountType(
    string accountTypeId,
    string platform,
    string displayName,
    string description,
    int sortOrder,
    string defaultDisplayName,
    DateTimeOffset now)
{
    var fields = CreateDefaultAccountTypeFields(defaultDisplayName);
    var runtime = CreateDefaultAccountTypeRuntime(platform);

    return new AccountTypeEntity
    {
        AccountTypeId = accountTypeId,
        Platform = platform,
        DisplayName = displayName,
        Description = description,
        WorkerProfileId = "test-worker",
        Enabled = true,
        SortOrder = sortOrder,
        FormFieldsJson = JsonSerializer.Serialize(fields),
        RuntimeConfigJson = JsonSerializer.Serialize(runtime),
        UpdatedAtUtc = now,
    };
}

static IReadOnlyList<AccountTypeFieldDto> CreateDefaultAccountTypeFields(string defaultDisplayName)
{
    return
    [
        new(
            "displayName",
            "Название аккаунта",
            "text",
            true,
            false,
            $"Например, {defaultDisplayName}",
            defaultDisplayName),
        new(
            "proxyHost",
            "Proxy host",
            "text",
            true,
            false,
            "45.88.208.237",
            null),
        new(
            "proxyPort",
            "Proxy port",
            "number",
            true,
            false,
            "1508",
            "1508"),
        new(
            "proxyLogin",
            "Proxy login",
            "text",
            true,
            false,
            "user305829",
            null),
        new(
            "proxyPassword",
            "Proxy password",
            "password",
            true,
            true,
            "Введите пароль",
            null),
    ];
}

static AccountTypeRuntimeConfigDto CreateDefaultAccountTypeRuntime(string platform)
{
    return new AccountTypeRuntimeConfigDto(
        AutospawnEnabled: true,
        WorkerImage: "ddcrm/worker-api:local",
        WorkerPathPrefix: "/internal/v2/worker",
        HealthPath: "/health",
        ContainerPort: 8080,
        EnvironmentVariables: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_URLS"] = "http://+:8080",
            ["TEST_USE_INMEMORY_DB"] = "true",
            ["TEST_WORKER_ENABLED"] = "true",
            ["TEST_WORKER_DEFAULT_VISIBILITY"] = "hidden",
            ["TEST_WORKER_ALLOWED_ENVIRONMENTS"] = "development,local,ci,staging",
            ["TEST_WORKER_BLOCK_IN_PRODUCTION"] = "true",
            ["TEST_WORKER_SCENARIO"] = "TW-SCN-HAPPY-PATH",
            ["TEST_WORKER_CAPABILITY_PROFILE"] = "TW-CAP-CORE-V1",
            ["TEST_WORKER_EXT_ACTIONS_ENABLED"] = "true",
            ["TEST_WORKER_PROVIDER"] = platform,
            ["WORKER_PROXY_CREDENTIALS_ENCRYPTION_KEY"] = "replace-with-long-random-worker-key",
            ["WORKER_API_SERVICE_AUTH_ENABLED"] = "true",
            ["WORKER_API_SERVICE_AUTH_ACCEPTED_TOKENS"] = "worker-token-a,worker-token-b",
        });
}

static bool ApplyAccountTypeDefaults(
    AccountTypeEntity current,
    AccountTypeEntity defaults,
    DateTimeOffset now)
{
    var hasChanges = false;

    if (!string.Equals(current.Platform, defaults.Platform, StringComparison.Ordinal))
    {
        current.Platform = defaults.Platform;
        hasChanges = true;
    }

    if (!string.Equals(current.DisplayName, defaults.DisplayName, StringComparison.Ordinal))
    {
        current.DisplayName = defaults.DisplayName;
        hasChanges = true;
    }

    if (!string.Equals(current.Description, defaults.Description, StringComparison.Ordinal))
    {
        current.Description = defaults.Description;
        hasChanges = true;
    }

    if (!string.Equals(current.WorkerProfileId, defaults.WorkerProfileId, StringComparison.Ordinal))
    {
        current.WorkerProfileId = defaults.WorkerProfileId;
        hasChanges = true;
    }

    if (current.Enabled != defaults.Enabled)
    {
        current.Enabled = defaults.Enabled;
        hasChanges = true;
    }

    if (current.SortOrder != defaults.SortOrder)
    {
        current.SortOrder = defaults.SortOrder;
        hasChanges = true;
    }

    if (!string.Equals(current.FormFieldsJson, defaults.FormFieldsJson, StringComparison.Ordinal))
    {
        current.FormFieldsJson = defaults.FormFieldsJson;
        hasChanges = true;
    }

    if (!string.Equals(current.RuntimeConfigJson, defaults.RuntimeConfigJson, StringComparison.Ordinal))
    {
        current.RuntimeConfigJson = defaults.RuntimeConfigJson;
        hasChanges = true;
    }

    if (hasChanges)
    {
        current.UpdatedAtUtc = now;
    }

    return hasChanges;
}

static LifecycleAuditEntity CreateAudit(WorkerPlacementEntity placement, string operation, string actor, string notes)
{
    return new LifecycleAuditEntity
    {
        Id = Guid.NewGuid(),
        AccountId = placement.AccountId,
        ProjectId = placement.ProjectId,
        Operation = operation,
        Actor = actor,
        Notes = notes,
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };
}

static string BuildRouteKey(Guid accountId) => $"rk.{accountId:N}";

static string NormalizeAccountTypeId(string accountTypeId)
{
    var normalized = accountTypeId.Trim();
    if (string.IsNullOrWhiteSpace(normalized))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "accountTypeId обязателен.");
    }

    if (normalized.Length > 120)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "accountTypeId превышает лимит длины.");
    }

    return normalized;
}

static string NormalizePlatform(string? platform, AccountTypeEntity? existing)
{
    var normalized = (platform ?? existing?.Platform)?.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(normalized))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "platform обязателен.");
    }

    if (normalized.Length > 80)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "platform превышает лимит длины.");
    }

    return normalized;
}

static string NormalizeAccountTypeDisplayName(string? displayName, AccountTypeEntity? existing)
{
    var normalized = (displayName ?? existing?.DisplayName)?.Trim();
    if (string.IsNullOrWhiteSpace(normalized))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "displayName обязателен.");
    }

    if (normalized.Length > 160)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "displayName превышает лимит длины.");
    }

    return normalized;
}

static string NormalizeWorkerProfileId(string? workerProfileId, AccountTypeEntity? existing)
{
    var normalized = (workerProfileId ?? existing?.WorkerProfileId)?.Trim();
    if (string.IsNullOrWhiteSpace(normalized))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "workerProfileId обязателен.");
    }

    if (normalized.Length > 80)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "workerProfileId превышает лимит длины.");
    }

    return normalized;
}

static IReadOnlyList<AccountTypeFieldDto> NormalizeFormFields(
    IReadOnlyList<AccountTypeFieldDto>? formFields,
    AccountTypeEntity? existing)
{
    if (formFields is { Count: > 0 })
    {
        return formFields;
    }

    if (!string.IsNullOrWhiteSpace(existing?.FormFieldsJson))
    {
        return DeserializeFormFields(existing.FormFieldsJson);
    }

    throw new ApiErrorException(
        StatusCodes.Status400BadRequest,
        ApiErrorCodes.ValidationError,
        "formFields обязателен и должен содержать минимум одно поле.");
}

static AccountTypeRuntimeConfigDto NormalizeRuntimeConfig(
    AccountTypeRuntimeConfigDto? runtime,
    AccountTypeEntity? existing,
    string platform)
{
    var candidate = runtime;
    if (candidate is null && !string.IsNullOrWhiteSpace(existing?.RuntimeConfigJson))
    {
        candidate = DeserializeRuntimeConfig(existing.RuntimeConfigJson, platform);
    }

    candidate ??= CreateDefaultAccountTypeRuntime(platform);

    var image = candidate.WorkerImage?.Trim();
    if (string.IsNullOrWhiteSpace(image))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "runtime.workerImage обязателен.");
    }

    var pathPrefix = string.IsNullOrWhiteSpace(candidate.WorkerPathPrefix)
        ? "/internal/v2/worker"
        : candidate.WorkerPathPrefix.Trim();
    if (!pathPrefix.StartsWith('/'))
    {
        pathPrefix = $"/{pathPrefix}";
    }

    var healthPath = string.IsNullOrWhiteSpace(candidate.HealthPath)
        ? "/health"
        : candidate.HealthPath.Trim();
    if (!healthPath.StartsWith('/'))
    {
        healthPath = $"/{healthPath}";
    }

    var containerPort = candidate.ContainerPort is < 1 or > 65535
        ? 8080
        : candidate.ContainerPort;
    var env = candidate.EnvironmentVariables is null
        ? new Dictionary<string, string>(StringComparer.Ordinal)
        : new Dictionary<string, string>(candidate.EnvironmentVariables, StringComparer.Ordinal);

    return new AccountTypeRuntimeConfigDto(
        candidate.AutospawnEnabled,
        image,
        pathPrefix,
        healthPath,
        containerPort,
        env);
}

static string SerializeFormFields(IReadOnlyList<AccountTypeFieldDto> formFields)
{
    return JsonSerializer.Serialize(formFields);
}

static string SerializeRuntimeConfig(AccountTypeRuntimeConfigDto runtime)
{
    return JsonSerializer.Serialize(runtime);
}

static AccountTypeRuntimeConfigDto DeserializeRuntimeConfig(string? json, string platform)
{
    if (string.IsNullOrWhiteSpace(json))
    {
        return CreateDefaultAccountTypeRuntime(platform);
    }

    try
    {
        return JsonSerializer.Deserialize<AccountTypeRuntimeConfigDto>(json)
               ?? CreateDefaultAccountTypeRuntime(platform);
    }
    catch
    {
        return CreateDefaultAccountTypeRuntime(platform);
    }
}

static AccountTypeRuntimeConfigDto ResolveRuntimeConfig(AccountTypeEntity entity, string platform)
{
    return NormalizeRuntimeConfig(
        DeserializeRuntimeConfig(entity.RuntimeConfigJson, platform),
        existing: null,
        platform);
}

static async Task<AccountTypeEntity> ResolveActiveAccountTypeForPlatformAsync(
    AccountsManagerDbContext dbContext,
    string platform,
    CancellationToken cancellationToken)
{
    var normalizedPlatform = platform.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(normalizedPlatform))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "platform обязателен для поиска account-type.");
    }

    var match = await dbContext.AccountTypes
        .AsNoTracking()
        .Where(x => x.Enabled)
        .Where(x => x.Platform == normalizedPlatform)
        .OrderBy(x => x.SortOrder)
        .ThenBy(x => x.AccountTypeId)
        .FirstOrDefaultAsync(cancellationToken);
    if (match is null)
    {
        throw new ApiErrorException(
            StatusCodes.Status409Conflict,
            ApiErrorCodes.Conflict,
            $"Нет активного account-type для платформы `{platform}`.");
    }

    return match;
}

static string BuildSpawnWorkerId(Guid accountId)
{
    return $"ddcrm-wk-{accountId:N}";
}

static string NormalizeFallbackWorkerId(string fallbackWorkerId)
{
    var normalized = fallbackWorkerId.Trim();
    if (string.IsNullOrWhiteSpace(normalized))
    {
        return "worker-api";
    }

    return normalized.Length > 63
        ? normalized[..63]
        : normalized;
}

static bool IsManagedWorker(string workerId)
{
    return workerId.StartsWith("ddcrm-wk-", StringComparison.OrdinalIgnoreCase);
}

static Dictionary<string, string> BuildWorkerSpawnEnvironment(
    AccountTypeRuntimeConfigDto runtimeConfig,
    string platform,
    Guid accountId)
{
    var env = new Dictionary<string, string>(runtimeConfig.EnvironmentVariables, StringComparer.Ordinal)
    {
        ["TEST_WORKER_PROVIDER"] = platform,
        ["TEST_INMEMORY_DB_NAME"] = $"worker-{accountId:N}",
    };

    if (!env.ContainsKey("ASPNETCORE_URLS"))
    {
        env["ASPNETCORE_URLS"] = $"http://+:{runtimeConfig.ContainerPort}";
    }

    return env;
}

static string ResolveWorkerControlBaseUrlTemplate(
    string workerId,
    WorkerServerEntity? server,
    int workerPort,
    string? preferredBaseUrlTemplate)
{
    if (!string.IsNullOrWhiteSpace(preferredBaseUrlTemplate))
    {
        return ApplyWorkerTemplatePlaceholders(preferredBaseUrlTemplate, workerId, workerPort);
    }

    if (!string.IsNullOrWhiteSpace(server?.BaseUrlTemplate))
    {
        return ApplyWorkerTemplatePlaceholders(server.BaseUrlTemplate, workerId, workerPort);
    }

    return $"http://{workerId}:{workerPort}";
}

static string ApplyWorkerTemplatePlaceholders(string template, string workerId, int workerPort)
{
    return template
        .Replace("{workerId}", workerId, StringComparison.OrdinalIgnoreCase)
        .Replace("{workerPort}", workerPort.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
}

static int ResolveWorkerPort(AccountTypeRuntimeConfigDto runtimeConfig, int fallbackPort)
{
    if (runtimeConfig.ContainerPort is >= 1 and <= 65535)
    {
        return runtimeConfig.ContainerPort;
    }

    if (fallbackPort is >= 1 and <= 65535)
    {
        return fallbackPort;
    }

    return 8080;
}

static AccountTypeDto ToAccountTypeDto(AccountTypeEntity entity)
{
    var runtime = DeserializeRuntimeConfig(entity.RuntimeConfigJson, entity.Platform);
    return new AccountTypeDto(
        entity.AccountTypeId,
        entity.Platform,
        entity.DisplayName,
        entity.Description,
        entity.WorkerProfileId,
        entity.Enabled,
        entity.SortOrder,
        DeserializeFormFields(entity.FormFieldsJson),
        runtime);
}

static IReadOnlyList<AccountTypeFieldDto> DeserializeFormFields(string? json)
{
    if (string.IsNullOrWhiteSpace(json))
    {
        return [];
    }

    try
    {
        return JsonSerializer.Deserialize<List<AccountTypeFieldDto>>(json) ?? [];
    }
    catch
    {
        return [];
    }
}

static WorkerServerSelection ResolveCreateTargetServer(IReadOnlyCollection<WorkerServerEntity> availableServers)
{
    if (availableServers.Count == 0)
    {
        return new WorkerServerSelection("srv-default", null, null);
    }

    var selected = SelectLeastLoadedServer(availableServers, excludedServerId: null);
    if (selected is null)
    {
        throw new ApiErrorException(
            StatusCodes.Status503ServiceUnavailable,
            ApiErrorCodes.InternalError,
            "Нет доступных healthy worker server для lifecycle create.");
    }

    return new WorkerServerSelection(selected.ServerId, selected.BaseUrlTemplate, selected);
}

static WorkerServerSelection ResolveMigrateTargetServer(
    IReadOnlyCollection<WorkerServerEntity> availableServers,
    WorkerPlacementEntity existingPlacement,
    string? requestedTargetServerId)
{
    if (!string.IsNullOrWhiteSpace(requestedTargetServerId))
    {
        var targetServerId = requestedTargetServerId.Trim();
        if (string.Equals(targetServerId, existingPlacement.ServerId, StringComparison.Ordinal))
        {
            throw new ApiErrorException(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.Conflict,
                "targetServerId совпадает с текущим serverId.");
        }

        if (availableServers.Count == 0)
        {
            return new WorkerServerSelection(targetServerId, null, null);
        }

        var explicitTarget = FindServer(availableServers, targetServerId);
        if (explicitTarget is null)
        {
            throw new ApiErrorException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound,
                "Указанный targetServerId отсутствует в registry.");
        }

        if (!IsServerEligibleForPlacement(explicitTarget))
        {
            throw new ApiErrorException(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.Conflict,
                "Указанный targetServerId недоступен для миграции (status/health/capacity).");
        }

        return new WorkerServerSelection(explicitTarget.ServerId, explicitTarget.BaseUrlTemplate, explicitTarget);
    }

    if (availableServers.Count == 0)
    {
        return new WorkerServerSelection("srv-default", null, null);
    }

    var selected = SelectLeastLoadedServer(availableServers, existingPlacement.ServerId);
    if (selected is null)
    {
        throw new ApiErrorException(
            StatusCodes.Status409Conflict,
            ApiErrorCodes.Conflict,
            "Нет доступного worker server для lifecycle migrate.");
    }

    return new WorkerServerSelection(selected.ServerId, selected.BaseUrlTemplate, selected);
}

static WorkerServerEntity? SelectLeastLoadedServer(
    IEnumerable<WorkerServerEntity> servers,
    string? excludedServerId)
{
    return servers
        .Where(x => excludedServerId is null || !string.Equals(x.ServerId, excludedServerId, StringComparison.Ordinal))
        .Where(IsServerEligibleForPlacement)
        .OrderBy(GetLoadRatio)
        .ThenBy(x => x.CurrentLoad)
        .ThenBy(x => x.ServerId, StringComparer.Ordinal)
        .FirstOrDefault();
}

static bool IsServerEligibleForPlacement(WorkerServerEntity server)
{
    if (!string.Equals(server.Status, "active", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (!string.Equals(server.Health, "healthy", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (!IsHeartbeatFresh(server))
    {
        return false;
    }

    return server.Capacity <= 0 || server.CurrentLoad < server.Capacity;
}

static bool IsHeartbeatFresh(WorkerServerEntity server)
{
    if (server.LastHeartbeatAtUtc is null)
    {
        return true;
    }

    return DateTimeOffset.UtcNow - server.LastHeartbeatAtUtc.Value <= TimeSpan.FromMinutes(3);
}

static decimal GetLoadRatio(WorkerServerEntity server)
{
    if (server.Capacity <= 0)
    {
        return server.CurrentLoad;
    }

    return (decimal)server.CurrentLoad / Math.Max(1, server.Capacity);
}

static WorkerServerEntity? FindServer(IEnumerable<WorkerServerEntity> servers, string serverId)
{
    return servers.FirstOrDefault(x => string.Equals(x.ServerId, serverId, StringComparison.Ordinal));
}

static void IncrementServerLoad(WorkerServerEntity? server)
{
    if (server is null)
    {
        return;
    }

    server.CurrentLoad = Math.Max(0, server.CurrentLoad + 1);
    server.UpdatedAtUtc = DateTimeOffset.UtcNow;
}

static void DecrementServerLoad(WorkerServerEntity? server)
{
    if (server is null)
    {
        return;
    }

    server.CurrentLoad = Math.Max(0, server.CurrentLoad - 1);
    server.UpdatedAtUtc = DateTimeOffset.UtcNow;
}

static string NormalizeServerId(string serverId)
{
    var normalized = serverId.Trim();
    if (string.IsNullOrWhiteSpace(normalized))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "serverId обязателен.");
    }

    if (normalized.Length > 120)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "serverId превышает лимит длины.");
    }

    return normalized;
}

static string NormalizeBaseUrlTemplate(string? baseUrlTemplate, WorkerServerEntity? existing)
{
    var value = (baseUrlTemplate ?? existing?.BaseUrlTemplate)?.Trim();
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "baseUrlTemplate обязателен.");
    }

    if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "baseUrlTemplate должен начинаться с http:// или https://.");
    }

    if (value.Length > 512)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "baseUrlTemplate превышает лимит длины.");
    }

    return value;
}

static string NormalizeWorkerServerStatus(string? status, WorkerServerEntity? existing)
{
    var normalized = (status ?? existing?.Status ?? "active").Trim().ToLowerInvariant();
    if (normalized is not ("active" or "draining" or "inactive"))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "status должен быть одним из: active, draining, inactive.");
    }

    return normalized;
}

static string NormalizeWorkerServerHealth(string? health, WorkerServerEntity? existing)
{
    var normalized = (health ?? existing?.Health ?? "healthy").Trim().ToLowerInvariant();
    if (normalized is not ("healthy" or "degraded" or "unhealthy"))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "health должен быть одним из: healthy, degraded, unhealthy.");
    }

    return normalized;
}

static int NormalizeCapacity(int? capacity, WorkerServerEntity? existing)
{
    var normalized = capacity ?? existing?.Capacity ?? 0;
    if (normalized < 0)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "capacity не может быть отрицательным.");
    }

    return normalized;
}

static int NormalizeCurrentLoad(int? currentLoad, WorkerServerEntity? existing, int capacity)
{
    var normalized = currentLoad ?? existing?.CurrentLoad ?? 0;
    if (normalized < 0)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "currentLoad не может быть отрицательным.");
    }

    if (capacity > 0 && normalized > capacity)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "currentLoad не может превышать capacity.");
    }

    return normalized;
}

static string? NormalizeDockerHost(string? dockerHost, WorkerServerEntity? existing)
{
    var normalized = dockerHost?.Trim();
    if (string.IsNullOrWhiteSpace(normalized))
    {
        return existing?.DockerHost;
    }

    if (!normalized.StartsWith("unix://", StringComparison.OrdinalIgnoreCase)
        && !normalized.StartsWith("npipe://", StringComparison.OrdinalIgnoreCase)
        && !normalized.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "dockerHost должен начинаться с unix://, npipe:// или tcp://.");
    }

    if (normalized.Length > 512)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "dockerHost превышает лимит длины.");
    }

    return normalized;
}

static string? NormalizeDockerNetwork(string? dockerNetwork, WorkerServerEntity? existing)
{
    var normalized = dockerNetwork?.Trim();
    if (string.IsNullOrWhiteSpace(normalized))
    {
        return existing?.DockerNetwork;
    }

    if (normalized.Length > 160)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "dockerNetwork превышает лимит длины.");
    }

    return normalized;
}

static byte[] ResolveRegistrySecretsEncryptionKey(string? rawKey)
{
    var source = string.IsNullOrWhiteSpace(rawKey)
        ? "ddcrm-local-account-manager-registry-secrets-key"
        : rawKey.Trim();

    if (source.Length < 32)
    {
        throw new InvalidOperationException(
            "ACCOUNT_MANAGER_AUTOSPAWN_REGISTRY_SECRET_ENCRYPTION_KEY должен содержать минимум 32 символа.");
    }

    return SHA256.HashData(Encoding.UTF8.GetBytes(source));
}

static WorkerServerRegistryState NormalizeWorkerServerRegistry(
    WorkerServerRegistryUpsertRequest? registry,
    WorkerServerEntity? existing,
    byte[] encryptionKey,
    DateTimeOffset now)
{
    var existingHost = string.IsNullOrWhiteSpace(existing?.RegistryHost)
        ? "ghcr.io"
        : existing.RegistryHost;

    var enabled = registry?.Enabled ?? existing?.RegistryEnabled ?? false;
    var host = NormalizeRegistryHost(registry?.Host, existingHost, enabled);
    var username = NormalizeRegistryUsername(registry?.Username, existing?.RegistryUsername, enabled);

    var clearToken = registry?.ClearToken ?? false;
    var rawToken = registry?.Token;
    if (clearToken && !string.IsNullOrWhiteSpace(rawToken))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Нельзя одновременно передавать registry token и clearToken=true.");
    }

    var tokenEncrypted = existing?.RegistryTokenEncrypted;
    var tokenUpdatedAt = existing?.RegistryTokenUpdatedAtUtc;

    if (clearToken)
    {
        tokenEncrypted = null;
        tokenUpdatedAt = now;
    }
    else if (rawToken is not null)
    {
        var normalizedToken = rawToken.Trim();
        if (string.IsNullOrWhiteSpace(normalizedToken))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "registry token не может быть пустым.");
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "Для сохранения registry token требуется username.");
        }

        tokenEncrypted = EncryptRegistrySecret(normalizedToken, encryptionKey);
        tokenUpdatedAt = now;
    }

    return new WorkerServerRegistryState(enabled, host, username, tokenEncrypted, tokenUpdatedAt);
}

static string NormalizeRegistryHost(string? host, string? existingHost, bool enabled)
{
    var normalized = string.IsNullOrWhiteSpace(host)
        ? (string.IsNullOrWhiteSpace(existingHost) ? "ghcr.io" : existingHost.Trim())
        : host.Trim();

    if (normalized.Length > 160)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "registry host превышает лимит длины.");
    }

    if (!string.Equals(normalized, "ghcr.io", StringComparison.OrdinalIgnoreCase))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "В v1 поддерживается только registry host `ghcr.io`.");
    }

    if (enabled && string.IsNullOrWhiteSpace(normalized))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Для enabled registry обязателен host.");
    }

    return normalized.ToLowerInvariant();
}

static string? NormalizeRegistryUsername(string? username, string? existingUsername, bool enabled)
{
    var normalized = username is null
        ? existingUsername?.Trim()
        : username.Trim();

    if (string.IsNullOrWhiteSpace(normalized))
    {
        return null;
    }

    if (normalized.Length > 160)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "registry username превышает лимит длины.");
    }

    if (enabled && string.IsNullOrWhiteSpace(normalized))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Для enabled registry обязателен username.");
    }

    return normalized;
}

static DockerRegistryAuthConfig? ResolveRegistryAuth(WorkerServerEntity? server, byte[] encryptionKey)
{
    if (server is null || !server.RegistryEnabled)
    {
        return null;
    }

    var host = string.IsNullOrWhiteSpace(server.RegistryHost)
        ? "ghcr.io"
        : server.RegistryHost.Trim().ToLowerInvariant();
    var username = server.RegistryUsername?.Trim();
    var token = string.IsNullOrWhiteSpace(server.RegistryTokenEncrypted)
        ? null
        : DecryptRegistrySecret(server.RegistryTokenEncrypted, encryptionKey);

    return new DockerRegistryAuthConfig(
        server.RegistryEnabled,
        host,
        username,
        token);
}

static string EncryptRegistrySecret(string plaintext, byte[] key)
{
    var nonce = RandomNumberGenerator.GetBytes(12);
    var plainBytes = Encoding.UTF8.GetBytes(plaintext);
    var cipherBytes = new byte[plainBytes.Length];
    var tag = new byte[16];

    using var aes = new AesGcm(key, tag.Length);
    aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

    var output = new byte[nonce.Length + tag.Length + cipherBytes.Length];
    Buffer.BlockCopy(nonce, 0, output, 0, nonce.Length);
    Buffer.BlockCopy(tag, 0, output, nonce.Length, tag.Length);
    Buffer.BlockCopy(cipherBytes, 0, output, nonce.Length + tag.Length, cipherBytes.Length);

    return Convert.ToBase64String(output);
}

static string DecryptRegistrySecret(string encodedCiphertext, byte[] key)
{
    try
    {
        var input = Convert.FromBase64String(encodedCiphertext);
        if (input.Length < 29)
        {
            throw new ApiErrorException(
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.InternalError,
                "Повреждённое registry credential значение в worker_servers.");
        }

        var nonce = input.AsSpan(0, 12).ToArray();
        var tag = input.AsSpan(12, 16).ToArray();
        var cipher = input.AsSpan(28).ToArray();
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }
    catch (ApiErrorException)
    {
        throw;
    }
    catch (Exception)
    {
        throw new ApiErrorException(
            StatusCodes.Status500InternalServerError,
            ApiErrorCodes.InternalError,
            "Не удалось расшифровать registry token для worker server.");
    }
}

static Dictionary<string, object?> NormalizeMetadata(
    Dictionary<string, object?>? metadata,
    WorkerServerEntity? existing)
{
    if (metadata is not null)
    {
        return metadata;
    }

    if (string.IsNullOrWhiteSpace(existing?.MetadataJson))
    {
        return [];
    }

    try
    {
        return JsonSerializer.Deserialize<Dictionary<string, object?>>(existing.MetadataJson) ?? [];
    }
    catch
    {
        return [];
    }
}

static string? SerializeMetadata(Dictionary<string, object?> metadata)
{
    return metadata.Count == 0
        ? null
        : JsonSerializer.Serialize(metadata);
}

static WorkerServerDto ToWorkerServerDto(WorkerServerEntity entity)
{
    Dictionary<string, object?> metadata;
    if (string.IsNullOrWhiteSpace(entity.MetadataJson))
    {
        metadata = [];
    }
    else
    {
        try
        {
            metadata = JsonSerializer.Deserialize<Dictionary<string, object?>>(entity.MetadataJson) ?? [];
        }
        catch
        {
            metadata = [];
        }
    }

    var registryHost = string.IsNullOrWhiteSpace(entity.RegistryHost)
        ? "ghcr.io"
        : entity.RegistryHost;
    var registry = new WorkerServerRegistrySummaryDto(
        entity.RegistryEnabled,
        registryHost,
        entity.RegistryUsername,
        !string.IsNullOrWhiteSpace(entity.RegistryTokenEncrypted),
        entity.RegistryTokenUpdatedAtUtc);

    return new WorkerServerDto(
        entity.ServerId,
        entity.BaseUrlTemplate,
        entity.Status,
        entity.Health,
        entity.Capacity,
        entity.CurrentLoad,
        entity.DockerHost,
        entity.DockerNetwork,
        entity.LastHeartbeatAtUtc,
        registry,
        metadata);
}

public sealed record AckResponse(string RequestId, string Status);

public sealed record LifecycleCreateRequest(Guid AccountId, Guid ProjectId, string Platform, Dictionary<string, object?> ProxyConfig);

public sealed record LifecycleUpdateRequest(Guid AccountId, Dictionary<string, object?>? ProxyConfig);

public sealed record LifecycleDeleteRequest(Guid AccountId);

public sealed record LifecycleMigrateRequest(Guid AccountId, string? TargetServerId);

public sealed record LifecycleRebalanceRequest(int? MaxMoves);

public sealed record LifecycleRebalanceMove(Guid AccountId, string FromServerId, string ToServerId, int RouteVersion);

public sealed record LifecycleRebalanceResponse(
    string RequestId,
    string Status,
    int Evaluated,
    int Moved,
    IReadOnlyList<LifecycleRebalanceMove> Migrations);

public sealed record AccountTypeFieldDto(
    string Key,
    string Label,
    string InputType,
    bool Required,
    bool Secret,
    string? Placeholder,
    string? DefaultValue);

public sealed record AccountTypeRuntimeConfigDto(
    bool AutospawnEnabled,
    string WorkerImage,
    string WorkerPathPrefix,
    string HealthPath,
    int ContainerPort,
    IReadOnlyDictionary<string, string> EnvironmentVariables);

public sealed record AccountTypeDto(
    string AccountTypeId,
    string Platform,
    string DisplayName,
    string? Description,
    string WorkerProfileId,
    bool Enabled,
    int SortOrder,
    IReadOnlyList<AccountTypeFieldDto> FormFields,
    AccountTypeRuntimeConfigDto Runtime);

public sealed record AccountTypeUpsertRequest(
    string? Platform,
    string? DisplayName,
    string? Description,
    string? WorkerProfileId,
    bool? Enabled,
    int? SortOrder,
    IReadOnlyList<AccountTypeFieldDto>? FormFields,
    AccountTypeRuntimeConfigDto? Runtime);

public sealed record AccountTypeListResponse(string RequestId, IReadOnlyList<AccountTypeDto> Items);

public sealed record AccountTypeResponse(string RequestId, AccountTypeDto AccountType);

public sealed record WorkerServerUpsertRequest(
    string? BaseUrlTemplate,
    string? Status,
    int? Capacity,
    int? CurrentLoad,
    string? Health,
    string? DockerHost,
    string? DockerNetwork,
    WorkerServerRegistryUpsertRequest? Registry,
    Dictionary<string, object?>? Metadata);

public sealed record WorkerServerRegistryUpsertRequest(
    bool? Enabled,
    string? Host,
    string? Username,
    string? Token,
    bool? ClearToken);

public sealed record WorkerServerHeartbeatRequest(
    string? Status,
    int? Capacity,
    int? CurrentLoad,
    string? Health,
    string? DockerHost,
    string? DockerNetwork,
    Dictionary<string, object?>? Metadata);

public sealed record WorkerServerDto(
    string ServerId,
    string BaseUrlTemplate,
    string Status,
    string Health,
    int Capacity,
    int CurrentLoad,
    string? DockerHost,
    string? DockerNetwork,
    DateTimeOffset? LastHeartbeatAtUtc,
    WorkerServerRegistrySummaryDto Registry,
    IReadOnlyDictionary<string, object?> Metadata);

public sealed record WorkerServerRegistrySummaryDto(
    bool Enabled,
    string Host,
    string? Username,
    bool HasToken,
    DateTimeOffset? TokenUpdatedAtUtc);

public sealed record WorkerServerResponse(string RequestId, WorkerServerDto WorkerServer);

public sealed record WorkerServerListResponse(string RequestId, IReadOnlyList<WorkerServerDto> Items);

public sealed record WorkerServerSelection(string ServerId, string? BaseUrlTemplate, WorkerServerEntity? Server);

public sealed record WorkerServerRegistryState(
    bool Enabled,
    string Host,
    string? Username,
    string? TokenEncrypted,
    DateTimeOffset? TokenUpdatedAtUtc);

public partial class Program;
