using System.Text.Json;
using DDCRM.Core.Persistence;
using DDCRM.Core.Persistence.Entities;
using DDCRM.Shared.Auth;
using DDCRM.Shared.Authorization;
using DDCRM.Shared.Errors;
using DDCRM.Shared.Extensions;
using DDCRM.Shared.Idempotency;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CoreDbContext>((serviceProvider, options) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var useInMemoryDb = configuration.GetValue("TEST_USE_INMEMORY_DB", false);

    if (useInMemoryDb)
    {
        options.UseInMemoryDatabase(configuration["TEST_INMEMORY_DB_NAME"] ?? "ddcrm-iam-tests");
        return;
    }

    options.UseNpgsql(
        configuration.GetConnectionString("CoreDb")
        ?? configuration["CORE_DB_CONNECTION"]
        ?? "Host=localhost;Port=5432;Database=ddcrm_core;Username=postgres;Password=postgres");
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
    var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
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
    Results.Ok(new GenericObjectResponse(
        httpContext.GetOrCreateRequestId(),
        new Dictionary<string, object?>
        {
            ["status"] = "ok",
        })));

var iam = app.MapGroup("/internal/v1/iam");

iam.MapPost("/check-permission", async (
    HttpContext httpContext,
    Dictionary<string, JsonElement> request,
    CoreDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var projectId = ReadGuid(request, "projectId");
    var userId = ReadGuid(request, "userId");
    var permission = ReadString(request, "permission");

    var role = await dbContext.ProjectMembers
        .Where(x => x.ProjectId == projectId && x.UserId == userId)
        .Select(x => x.Role)
        .SingleOrDefaultAsync(cancellationToken);

    var allowed = !string.IsNullOrWhiteSpace(role) && PermissionMatrix.HasPermission(role, permission);

    return Results.Ok(new GenericObjectResponse(
        httpContext.GetOrCreateRequestId(),
        new Dictionary<string, object?>
        {
            ["allowed"] = allowed,
            ["role"] = role,
            ["permission"] = permission,
            ["projectId"] = projectId,
            ["userId"] = userId,
        }));
});

iam.MapGet("/project-membership/{projectId:guid}/{userId:guid}", async (
    HttpContext httpContext,
    Guid projectId,
    Guid userId,
    CoreDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var role = await dbContext.ProjectMembers
        .Where(x => x.ProjectId == projectId && x.UserId == userId)
        .Select(x => x.Role)
        .SingleOrDefaultAsync(cancellationToken);

    return Results.Ok(new GenericObjectResponse(
        httpContext.GetOrCreateRequestId(),
        new Dictionary<string, object?>
        {
            ["projectId"] = projectId,
            ["userId"] = userId,
            ["isMember"] = role is not null,
            ["role"] = role,
        }));
});

iam.MapPost("/membership-cache/invalidate", async (
    HttpContext httpContext,
    Dictionary<string, JsonElement> request,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    var actor = ReadOptionalString(request, "actor") ?? "iam-system";
    var reason = ReadOptionalString(request, "reason") ?? "manual-invalidation";

    var projectId = TryReadGuid(request, "projectId");
    var userId = TryReadGuid(request, "userId");

    return await idempotency.ExecuteAsync(
        dbContext,
        "iam:membershipCacheInvalidate",
        idempotencyKey,
        async ct =>
        {
            dbContext.MembershipCacheInvalidationAudits.Add(new MembershipCacheInvalidationAuditEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                UserId = userId,
                Actor = actor,
                Reason = reason,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });

            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
        },
        cancellationToken);
});

app.MapPost("/internal/v1/lifecycle/create", (HttpContext context) => ThrowFeatureNotReadyWithIdempotency(context, "lifecycleCreate"));
app.MapPost("/internal/v1/lifecycle/update", (HttpContext context) => ThrowFeatureNotReadyWithIdempotency(context, "lifecycleUpdate"));
app.MapPost("/internal/v1/lifecycle/delete", (HttpContext context) => ThrowFeatureNotReadyWithIdempotency(context, "lifecycleDelete"));
app.MapPost("/internal/v1/lifecycle/migrate", (HttpContext context) => ThrowFeatureNotReadyWithIdempotency(context, "lifecycleMigrate"));
app.MapPost("/internal/v1/payments/create", (HttpContext context) => ThrowFeatureNotReadyWithIdempotency(context, "internalPaymentsCreate"));
app.MapPost("/internal/v1/payments/webhook", (HttpContext context) => ThrowFeatureNotReady(context, "internalPaymentsWebhook"));
app.MapPost("/internal/v1/payments/refund", (HttpContext context) => ThrowFeatureNotReadyWithIdempotency(context, "internalPaymentsRefund"));
app.MapPost("/internal/v1/subscriptions/manual-activate", (HttpContext context) => ThrowFeatureNotReadyWithIdempotency(context, "internalSubscriptionsManualActivate"));
app.MapPost("/internal/v1/subscriptions/reconcile", (HttpContext context) => ThrowFeatureNotReady(context, "internalSubscriptionsReconcile"));
app.MapPost("/internal/v1/entitlement/check", (HttpContext context) => ThrowFeatureNotReady(context, "entitlementCheck"));
app.MapPost("/internal/v1/entitlement/recalculate", (HttpContext context) => ThrowFeatureNotReadyWithIdempotency(context, "entitlementRecalculate"));
app.MapPost("/internal/v1/entitlement/override", (HttpContext context) => ThrowFeatureNotReadyWithIdempotency(context, "entitlementOverride"));
app.MapGet("/internal/v1/entitlement/{projectId:guid}", (HttpContext context) => ThrowFeatureNotReady(context, "entitlementGet"));

app.Run();

return;

static Guid ReadGuid(Dictionary<string, JsonElement> payload, string key)
{
    if (!payload.TryGetValue(key, out var value) || !Guid.TryParse(value.GetString(), out var parsed))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, $"Поле {key} должно быть GUID.");
    }

    return parsed;
}

static Guid? TryReadGuid(Dictionary<string, JsonElement> payload, string key)
{
    if (!payload.TryGetValue(key, out var value))
    {
        return null;
    }

    return Guid.TryParse(value.GetString(), out var parsed) ? parsed : null;
}

static string ReadString(Dictionary<string, JsonElement> payload, string key)
{
    if (!payload.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value.GetString()))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, $"Поле {key} обязательно.");
    }

    return value.GetString()!.Trim();
}

static string? ReadOptionalString(Dictionary<string, JsonElement> payload, string key)
{
    if (!payload.TryGetValue(key, out var value))
    {
        return null;
    }

    var text = value.GetString();
    return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

static IResult ThrowFeatureNotReady(HttpContext httpContext, string operation)
{
    throw new ApiErrorException(
        StatusCodes.Status501NotImplemented,
        ApiErrorCodes.FeatureNotReady,
        $"Операция {operation} будет реализована в следующих work-packages.",
        new Dictionary<string, object?>
        {
            ["operation"] = operation,
            ["roadmapPhase"] = "phase-1-wave-1",
        });
}

static IResult ThrowFeatureNotReadyWithIdempotency(HttpContext httpContext, string operation)
{
    _ = httpContext.RequireIdempotencyKey();
    return ThrowFeatureNotReady(httpContext, operation);
}

public sealed record AckResponse(string RequestId, string Status);

public sealed record GenericObjectResponse(string RequestId, IDictionary<string, object?> Data);

public partial class Program;
