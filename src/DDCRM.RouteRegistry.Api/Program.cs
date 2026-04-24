using System.Text.Json;
using DDCRM.RouteRegistry.Persistence;
using DDCRM.RouteRegistry.Persistence.Entities;
using DDCRM.Shared.Auth;
using DDCRM.Shared.Errors;
using DDCRM.Shared.Extensions;
using DDCRM.Shared.Idempotency;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<RouteRegistryDbContext>((serviceProvider, options) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var useInMemoryDb = configuration.GetValue("TEST_USE_INMEMORY_DB", false);

    if (useInMemoryDb)
    {
        options.UseInMemoryDatabase(configuration["TEST_INMEMORY_DB_NAME"] ?? "ddcrm-route-tests");
        return;
    }

    options.UseNpgsql(
        configuration.GetConnectionString("RouteRegistryDb")
        ?? configuration["ROUTE_REGISTRY_DB_CONNECTION"]
        ?? "Host=localhost;Port=5432;Database=ddcrm_route_registry;Username=postgres;Password=postgres");
});

builder.Services.AddScoped<IdempotencyExecutor>();
builder.Services.AddServiceTokenAuth(options =>
{
    options.Enabled = builder.Configuration.GetValue("INTERNAL_API_SERVICE_AUTH_ENABLED", true);
    options.AcceptedTokens = builder.Configuration.GetCommaSeparatedValues("INTERNAL_API_SERVICE_AUTH_ACCEPTED_TOKENS");
    options.ForbiddenTokens = builder.Configuration.GetCommaSeparatedValues("WORKER_API_SERVICE_AUTH_ACCEPTED_TOKENS");
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RouteRegistryDbContext>();
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

var routes = app.MapGroup("/internal/v1/routes");

routes.MapPut("/{accountId:guid}", async (
    HttpContext httpContext,
    Guid accountId,
    RouteUpsertRequest request,
    RouteRegistryDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    return await idempotency.ExecuteAsync(
        dbContext,
        $"route:upsert:{accountId}",
        idempotencyKey,
        async ct =>
        {
            var byRoute = await dbContext.RouteRecords
                .SingleOrDefaultAsync(x => x.RouteKey == request.RouteKey && x.AccountId != accountId, ct);
            if (byRoute is not null)
            {
                throw new ApiErrorException(StatusCodes.Status409Conflict, ApiErrorCodes.Conflict, "RouteKey уже используется другим аккаунтом.");
            }

            var existing = await dbContext.RouteRecords.SingleOrDefaultAsync(x => x.AccountId == accountId, ct);
            if (existing is null)
            {
                dbContext.RouteRecords.Add(new RouteRecordEntity
                {
                    AccountId = accountId,
                    ProjectId = request.ProjectId,
                    RouteKey = request.RouteKey,
                    ServerId = request.WorkerBinding.ServerId,
                    WorkerId = request.WorkerBinding.WorkerId,
                    PodId = request.WorkerBinding.PodId,
                    RouteVersion = request.RouteVersion,
                    IsActive = true,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                });
            }
            else
            {
                if (request.RouteVersion < existing.RouteVersion)
                {
                    throw new ApiErrorException(StatusCodes.Status409Conflict, ApiErrorCodes.Conflict, "Версия маршрута устарела.");
                }

                existing.ProjectId = request.ProjectId;
                existing.RouteKey = request.RouteKey;
                existing.ServerId = request.WorkerBinding.ServerId;
                existing.WorkerId = request.WorkerBinding.WorkerId;
                existing.PodId = request.WorkerBinding.PodId;
                existing.RouteVersion = request.RouteVersion;
                existing.IsActive = true;
                existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(StatusCodes.Status200OK, new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
        },
        cancellationToken);
});

routes.MapDelete("/{accountId:guid}", async (
    HttpContext httpContext,
    Guid accountId,
    RouteRegistryDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    return await idempotency.ExecuteAsync(
        dbContext,
        $"route:delete:{accountId}",
        idempotencyKey,
        async ct =>
        {
            var existing = await dbContext.RouteRecords.SingleOrDefaultAsync(x => x.AccountId == accountId, ct);
            if (existing is not null)
            {
                dbContext.RouteRecords.Remove(existing);
                await dbContext.SaveChangesAsync(ct);
            }

            return new IdempotentExecutionResult(StatusCodes.Status200OK, new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
        },
        cancellationToken);
});

routes.MapPost("/{accountId:guid}/switch", async (
    HttpContext httpContext,
    Guid accountId,
    RouteSwitchRequest request,
    RouteRegistryDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    return await idempotency.ExecuteAsync(
        dbContext,
        $"route:switch:{accountId}",
        idempotencyKey,
        async ct =>
        {
            var existing = await dbContext.RouteRecords.SingleOrDefaultAsync(x => x.AccountId == accountId, ct);
            if (existing is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Маршрут не найден.");
            }

            if (request.RouteVersion <= existing.RouteVersion)
            {
                throw new ApiErrorException(StatusCodes.Status409Conflict, ApiErrorCodes.Conflict, "Версия маршрута должна быть больше текущей.");
            }

            existing.ServerId = request.WorkerBinding.ServerId;
            existing.WorkerId = request.WorkerBinding.WorkerId;
            existing.PodId = request.WorkerBinding.PodId;
            existing.RouteVersion = request.RouteVersion;
            existing.IsActive = true;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(StatusCodes.Status200OK, new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
        },
        cancellationToken);
});

routes.MapPost("/resolve-bulk", async (
    HttpContext httpContext,
    RouteResolveBulkRequest request,
    RouteRegistryDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (request.RouteKeys.Count == 0)
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Требуется минимум один routeKey.");
    }

    var records = await dbContext.RouteRecords
        .Where(x => request.RouteKeys.Contains(x.RouteKey))
        .ToListAsync(cancellationToken);

    var items = records
        .Select(x => new Dictionary<string, object?>
        {
            ["routeKey"] = x.RouteKey,
            ["accountId"] = x.AccountId,
            ["projectId"] = x.ProjectId,
            ["routeVersion"] = x.RouteVersion,
            ["workerBinding"] = new Dictionary<string, object?>
            {
                ["serverId"] = x.ServerId,
                ["workerId"] = x.WorkerId,
                ["podId"] = x.PodId,
            },
        })
        .ToList();

    return Results.Ok(new GenericObjectResponse(
        httpContext.GetOrCreateRequestId(),
        new Dictionary<string, object?>
        {
            ["items"] = items,
        }));
});

app.Run();

public sealed record WorkerBinding(string ServerId, string WorkerId, string? PodId);

public sealed record RouteUpsertRequest(Guid ProjectId, string RouteKey, WorkerBinding WorkerBinding, int RouteVersion);

public sealed record RouteSwitchRequest(WorkerBinding WorkerBinding, int RouteVersion);

public sealed record RouteResolveBulkRequest(IReadOnlyCollection<string> RouteKeys);

public sealed record AckResponse(string RequestId, string Status);

public sealed record GenericObjectResponse(string RequestId, IDictionary<string, object?> Data);

public partial class Program;
