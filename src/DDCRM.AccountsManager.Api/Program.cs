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

builder.Services.AddHttpClient<IRouteRegistryClient, RouteRegistryHttpClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<RouteRegistryClientOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(options.BaseUrl))
    {
        client.BaseAddress = new Uri(options.BaseUrl);
    }
});
builder.Services.AddHttpClient<IWorkerControlClient, WorkerControlHttpClient>();

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
            var metadata = NormalizeMetadata(request.Metadata, existing);

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
                    MetadataJson = SerializeMetadata(metadata),
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
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
                existing.MetadataJson = SerializeMetadata(metadata);
                existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
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
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    if (string.IsNullOrWhiteSpace(request.Platform))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "platform обязателен для lifecycle create.");
    }

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

            var workerId = $"worker-{request.AccountId:N}";
            var podId = $"pod-{request.AccountId:N}";
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

            await workerControlClient.ApplyProxyCredentialsAsync(
                workerBinding,
                request.AccountId,
                request.ProxyConfig,
                idempotencyKey,
                targetServer.BaseUrlTemplate,
                ct);

            var entity = new WorkerPlacementEntity
            {
                AccountId = request.AccountId,
                ProjectId = request.ProjectId,
                Platform = request.Platform,
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

            await workerControlClient.ApplyProxyCredentialsAsync(
                new WorkerBindingDto(existing.ServerId, existing.WorkerId, existing.PodId),
                request.AccountId,
                request.ProxyConfig!,
                idempotencyKey,
                workerServer?.BaseUrlTemplate,
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

            var targetPodId = $"pod-{Guid.NewGuid():N}"[..16];
            var nextRouteVersion = existing.RouteVersion + 1;
            var previousServerId = existing.ServerId;

            await routeRegistryClient.SwitchAsync(
                request.AccountId,
                new RouteSwitchRequestDto(
                    new WorkerBindingDto(targetServer.ServerId, existing.WorkerId, targetPodId),
                    nextRouteVersion),
                idempotencyKey,
                ct);

            existing.ServerId = targetServer.ServerId;
            existing.PodId = targetPodId;
            existing.RouteVersion = nextRouteVersion;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;

            DecrementServerLoad(FindServer(availableServers, previousServerId));
            IncrementServerLoad(targetServer.Server);

            dbContext.LifecycleAudits.Add(CreateAudit(existing, "migrate", "accounts-manager", $"migrated to {targetServer.ServerId}"));
            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(StatusCodes.Status202Accepted, new AckResponse(httpContext.GetOrCreateRequestId(), "accepted"));
        },
        cancellationToken);
});

lifecycle.MapPost("/rebalance", async (
    HttpContext httpContext,
    LifecycleRebalanceRequest request,
    AccountsManagerDbContext dbContext,
    IRouteRegistryClient routeRegistryClient,
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
                var targetPodId = $"pod-{Guid.NewGuid():N}"[..16];

                await routeRegistryClient.SwitchAsync(
                    placement.AccountId,
                    new RouteSwitchRequestDto(
                        new WorkerBindingDto(targetServer.ServerId, placement.WorkerId, targetPodId),
                        nextRouteVersion),
                    idempotencyKey,
                    ct);

                var sourceServerId = placement.ServerId;
                placement.ServerId = targetServer.ServerId;
                placement.PodId = targetPodId;
                placement.RouteVersion = nextRouteVersion;
                placement.UpdatedAtUtc = DateTimeOffset.UtcNow;

                DecrementServerLoad(FindServer(servers, sourceServerId));
                IncrementServerLoad(targetServer);

                dbContext.LifecycleAudits.Add(CreateAudit(placement, "rebalance", "accounts-manager", $"rebalanced to {targetServer.ServerId}"));
                migrations.Add(new LifecycleRebalanceMove(placement.AccountId, sourceServerId, targetServer.ServerId, nextRouteVersion));
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
    if (await dbContext.AccountTypes.AnyAsync())
    {
        return;
    }

    var now = DateTimeOffset.UtcNow;
    dbContext.AccountTypes.AddRange(
        CreateDefaultAccountTypes(now));
    await dbContext.SaveChangesAsync();
}

static IReadOnlyList<AccountTypeEntity> CreateDefaultAccountTypes(DateTimeOffset now)
{
    var fields = new List<AccountTypeFieldDto>
    {
        new(
            "displayName",
            "Название аккаунта",
            "text",
            true,
            false,
            "Например, FunPay Test Account",
            "FunPay Test Account"),
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
    };

    return
    [
        new AccountTypeEntity
        {
            AccountTypeId = "test-worker.funpay",
            Platform = "funpay",
            DisplayName = "Тестовый worker: FunPay",
            Description = "Единственный доступный тип аккаунта на текущем этапе.",
            WorkerProfileId = "test-worker",
            Enabled = true,
            SortOrder = 10,
            FormFieldsJson = JsonSerializer.Serialize(fields),
            UpdatedAtUtc = now,
        },
    ];
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

static AccountTypeDto ToAccountTypeDto(AccountTypeEntity entity)
{
    return new AccountTypeDto(
        entity.AccountTypeId,
        entity.Platform,
        entity.DisplayName,
        entity.Description,
        entity.WorkerProfileId,
        entity.Enabled,
        entity.SortOrder,
        DeserializeFormFields(entity.FormFieldsJson));
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

    return new WorkerServerDto(
        entity.ServerId,
        entity.BaseUrlTemplate,
        entity.Status,
        entity.Health,
        entity.Capacity,
        entity.CurrentLoad,
        entity.LastHeartbeatAtUtc,
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

public sealed record AccountTypeDto(
    string AccountTypeId,
    string Platform,
    string DisplayName,
    string? Description,
    string WorkerProfileId,
    bool Enabled,
    int SortOrder,
    IReadOnlyList<AccountTypeFieldDto> FormFields);

public sealed record AccountTypeListResponse(string RequestId, IReadOnlyList<AccountTypeDto> Items);

public sealed record WorkerServerUpsertRequest(
    string? BaseUrlTemplate,
    string? Status,
    int? Capacity,
    int? CurrentLoad,
    string? Health,
    Dictionary<string, object?>? Metadata);

public sealed record WorkerServerHeartbeatRequest(
    string? Status,
    int? Capacity,
    int? CurrentLoad,
    string? Health,
    Dictionary<string, object?>? Metadata);

public sealed record WorkerServerDto(
    string ServerId,
    string BaseUrlTemplate,
    string Status,
    string Health,
    int Capacity,
    int CurrentLoad,
    DateTimeOffset? LastHeartbeatAtUtc,
    IReadOnlyDictionary<string, object?> Metadata);

public sealed record WorkerServerResponse(string RequestId, WorkerServerDto WorkerServer);

public sealed record WorkerServerListResponse(string RequestId, IReadOnlyList<WorkerServerDto> Items);

public sealed record WorkerServerSelection(string ServerId, string? BaseUrlTemplate, WorkerServerEntity? Server);

public partial class Program;
