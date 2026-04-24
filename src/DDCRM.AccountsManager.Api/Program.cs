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
}

app.UseDdcrmCommonPipeline();
app.UseServiceTokenAuth();

app.MapGet("/health", (HttpContext httpContext) =>
    Results.Ok(new
    {
        requestId = httpContext.GetOrCreateRequestId(),
        status = "ok",
    }));

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

            var workerId = $"worker-{request.AccountId:N}";
            var serverId = "srv-default";
            var podId = $"pod-{request.AccountId:N}";
            var routeVersion = 1;
            var workerBinding = new WorkerBindingDto(serverId, workerId, podId);

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
                ct);

            var entity = new WorkerPlacementEntity
            {
                AccountId = request.AccountId,
                ProjectId = request.ProjectId,
                Platform = request.Platform,
                WorkerId = workerId,
                ServerId = serverId,
                PodId = podId,
                LifecycleStatus = "active",
                ProxyConfigured = true,
                RouteVersion = routeVersion,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };

            dbContext.WorkerPlacements.Add(entity);
            dbContext.LifecycleAudits.Add(CreateAudit(entity, "create", "accounts-manager", "worker placement created"));
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

            await workerControlClient.ApplyProxyCredentialsAsync(
                new WorkerBindingDto(existing.ServerId, existing.WorkerId, existing.PodId),
                request.AccountId,
                request.ProxyConfig!,
                idempotencyKey,
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

            dbContext.LifecycleAudits.Add(CreateAudit(existing, "delete", "accounts-manager", "worker placement deleted"));
            dbContext.WorkerPlacements.Remove(existing);
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

            var targetServerId = ResolveTargetServerId(request.TargetServerId, request.AccountId);
            if (targetServerId.Length > 120)
            {
                throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "targetServerId превышает лимит длины.");
            }

            var targetPodId = $"pod-{Guid.NewGuid():N}"[..16];
            var nextRouteVersion = existing.RouteVersion + 1;

            await routeRegistryClient.SwitchAsync(
                request.AccountId,
                new RouteSwitchRequestDto(
                    new WorkerBindingDto(targetServerId, existing.WorkerId, targetPodId),
                    nextRouteVersion),
                idempotencyKey,
                ct);

            existing.ServerId = targetServerId;
            existing.PodId = targetPodId;
            existing.RouteVersion = nextRouteVersion;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;

            dbContext.LifecycleAudits.Add(CreateAudit(existing, "migrate", "accounts-manager", $"migrated to {targetServerId}"));
            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(StatusCodes.Status202Accepted, new AckResponse(httpContext.GetOrCreateRequestId(), "accepted"));
        },
        cancellationToken);
});

app.Run();

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

static string ResolveTargetServerId(string? targetServerId, Guid accountId)
{
    if (!string.IsNullOrWhiteSpace(targetServerId))
    {
        return targetServerId.Trim();
    }

    var generated = $"srv-migrated-{accountId:N}";
    return generated.Length <= 120 ? generated : generated[..120];
}

public sealed record AckResponse(string RequestId, string Status);

public sealed record LifecycleCreateRequest(Guid AccountId, Guid ProjectId, string Platform, Dictionary<string, object?> ProxyConfig);

public sealed record LifecycleUpdateRequest(Guid AccountId, Dictionary<string, object?>? ProxyConfig);

public sealed record LifecycleDeleteRequest(Guid AccountId);

public sealed record LifecycleMigrateRequest(Guid AccountId, string? TargetServerId);

public partial class Program;
