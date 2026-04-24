using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DDCRM.Core.Persistence;
using DDCRM.Core.Persistence.Entities;
using DDCRM.Shared.Authorization;
using DDCRM.Shared.Constants;
using DDCRM.Shared.Errors;
using DDCRM.Shared.Extensions;
using DDCRM.Shared.Idempotency;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CoreDbContext>((serviceProvider, options) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var useInMemoryDb = configuration.GetValue("TEST_USE_INMEMORY_DB", false);

    if (useInMemoryDb)
    {
        options.UseInMemoryDatabase(configuration["TEST_INMEMORY_DB_NAME"] ?? "ddcrm-core-tests");
        return;
    }

    options.UseNpgsql(
        configuration.GetConnectionString("CoreDb")
        ?? configuration["CORE_DB_CONNECTION"]
        ?? "Host=localhost;Port=5432;Database=ddcrm_core;Username=postgres;Password=postgres");
});

builder.Services.AddScoped<IdempotencyExecutor>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var issuer = builder.Configuration["EXTERNAL_API_JWT_ISSUER"] ?? "ddcrm-local";
        var audience = builder.Configuration["EXTERNAL_API_JWT_AUDIENCE"] ?? "ddcrm-api";
        var signingKey = builder.Configuration["EXTERNAL_API_JWT_SIGNING_KEY"]
                         ?? "replace-this-signing-key-with-at-least-32-characters";

        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy("external-cors", policy =>
    {
        var enabled = builder.Configuration.GetValue("EXTERNAL_API_CORS_ENABLED", true);
        if (!enabled)
        {
            return;
        }

        var origins = builder.Configuration.GetCommaSeparatedValues("EXTERNAL_API_CORS_ALLOW_ORIGINS");
        var methods = builder.Configuration.GetCommaSeparatedValues("EXTERNAL_API_CORS_ALLOW_METHODS");
        var headers = builder.Configuration.GetCommaSeparatedValues("EXTERNAL_API_CORS_ALLOW_HEADERS");
        var exposedHeaders = builder.Configuration.GetCommaSeparatedValues("EXTERNAL_API_CORS_EXPOSE_HEADERS");
        var maxAge = builder.Configuration.GetValue("EXTERNAL_API_CORS_MAX_AGE_SECONDS", 600);
        var allowCredentials = builder.Configuration.GetValue("EXTERNAL_API_CORS_ALLOW_CREDENTIALS", true);

        var originList = origins.Length > 0 ? origins : ["https://app.ddcrm.local"];
        policy.WithOrigins(originList);

        if (methods.Length > 0)
        {
            policy.WithMethods(methods);
        }

        if (headers.Length > 0)
        {
            policy.WithHeaders(headers);
        }

        if (exposedHeaders.Length > 0)
        {
            policy.WithExposedHeaders(exposedHeaders);
        }

        if (allowCredentials)
        {
            policy.AllowCredentials();
        }

        policy.SetPreflightMaxAge(TimeSpan.FromSeconds(maxAge));
    });
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
app.UseCors("external-cors");
app.UseAuthentication();
app.UseAuthorization();

app.MapMethods("/v1/{*path}", ["OPTIONS"], () => Results.Ok());

var external = app.MapGroup("/v1").RequireAuthorization();

external.MapGet("/projects", async (HttpContext httpContext, CoreDbContext dbContext, CancellationToken cancellationToken) =>
{
    var userId = GetCurrentUserId(httpContext);

    var projects = await dbContext.ProjectMembers
        .Where(x => x.UserId == userId)
        .Join(
            dbContext.Projects,
            member => member.ProjectId,
            project => project.Id,
            (member, project) => new ProjectDto(project.Id, project.Name, project.Status))
        .OrderBy(x => x.Name)
        .ToListAsync(cancellationToken);

    return Results.Ok(new ProjectListResponse(httpContext.GetOrCreateRequestId(), projects));
});

external.MapPost("/projects", async (
    HttpContext httpContext,
    ProjectCreateRequest request,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var userId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    if (string.IsNullOrWhiteSpace(request.Name))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Поле name обязательно.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        "core:createProject",
        idempotencyKey,
        async ct =>
        {
            var project = new ProjectEntity
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Status = "active",
                OwnerUserId = userId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };

            dbContext.Projects.Add(project);
            dbContext.ProjectMembers.Add(new ProjectMemberEntity
            {
                ProjectId = project.Id,
                UserId = userId,
                Role = ProjectRoles.Owner,
                JoinedAtUtc = DateTimeOffset.UtcNow,
            });

            await dbContext.SaveChangesAsync(ct);

            var payload = new ProjectResponse(
                httpContext.GetOrCreateRequestId(),
                new ProjectDto(project.Id, project.Name, project.Status));

            return new IdempotentExecutionResult(StatusCodes.Status201Created, payload);
        },
        cancellationToken);
});

external.MapGet("/projects/{projectId:guid}", async (
    HttpContext httpContext,
    Guid projectId,
    CoreDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var userId = GetCurrentUserId(httpContext);
    await EnsureProjectMembershipAsync(dbContext, projectId, userId, cancellationToken);

    var project = await dbContext.Projects.SingleOrDefaultAsync(x => x.Id == projectId, cancellationToken);
    if (project is null)
    {
        throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Проект не найден.");
    }

    return Results.Ok(new ProjectResponse(
        httpContext.GetOrCreateRequestId(),
        new ProjectDto(project.Id, project.Name, project.Status)));
});

external.MapPatch("/projects/{projectId:guid}", async (
    HttpContext httpContext,
    Guid projectId,
    Dictionary<string, JsonElement> request,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var userId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    var role = await RequireProjectRoleAsync(dbContext, projectId, userId, cancellationToken);
    if (role is not ProjectRoles.Owner and not ProjectRoles.Admin)
    {
        throw new ApiErrorException(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "Недостаточно прав для обновления проекта.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:updateProject:{projectId}",
        idempotencyKey,
        async ct =>
        {
            var project = await dbContext.Projects.SingleOrDefaultAsync(x => x.Id == projectId, ct);
            if (project is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Проект не найден.");
            }

            if (request.TryGetValue("name", out var nameValue))
            {
                var name = nameValue.GetString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Поле name не может быть пустым.");
                }

                project.Name = name.Trim();
            }

            if (request.TryGetValue("status", out var statusValue))
            {
                var status = statusValue.GetString();
                if (string.IsNullOrWhiteSpace(status))
                {
                    throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Поле status не может быть пустым.");
                }

                project.Status = status.Trim();
            }

            await dbContext.SaveChangesAsync(ct);

            var payload = new ProjectResponse(
                httpContext.GetOrCreateRequestId(),
                new ProjectDto(project.Id, project.Name, project.Status));

            return new IdempotentExecutionResult(StatusCodes.Status200OK, payload);
        },
        cancellationToken);
});

external.MapPost("/projects/{projectId:guid}/members", async (
    HttpContext httpContext,
    Guid projectId,
    Dictionary<string, JsonElement> request,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectMembersInvite, cancellationToken);

    var userId = ReadGuid(request, "userId");
    var role = ReadString(request, "role");

    if (!ProjectRoles.All.Contains(role))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Некорректная роль участника.");
    }

    if (role == ProjectRoles.Owner)
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Роль owner задается через changeMemberRole.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:addMember:{projectId}:{userId}",
        idempotencyKey,
        async ct =>
        {
            var existing = await dbContext.ProjectMembers
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.UserId == userId, ct);
            if (existing is not null)
            {
                throw new ApiErrorException(StatusCodes.Status409Conflict, ApiErrorCodes.Conflict, "Участник уже состоит в проекте.");
            }

            dbContext.ProjectMembers.Add(new ProjectMemberEntity
            {
                ProjectId = projectId,
                UserId = userId,
                Role = role,
                JoinedAtUtc = DateTimeOffset.UtcNow,
            });

            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
        },
        cancellationToken);
});

external.MapPatch("/projects/{projectId:guid}/members/{userId:guid}/role", async (
    HttpContext httpContext,
    Guid projectId,
    Guid userId,
    RoleChangeRequest request,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectRolesChange, cancellationToken);

    if (!ProjectRoles.All.Contains(request.Role))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Некорректная роль.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:changeRole:{projectId}:{userId}",
        idempotencyKey,
        async ct =>
        {
            var member = await dbContext.ProjectMembers
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.UserId == userId, ct);

            if (member is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Участник не найден.");
            }

            if (request.Role == ProjectRoles.Owner)
            {
                var currentOwner = await dbContext.ProjectMembers
                    .SingleAsync(x => x.ProjectId == projectId && x.Role == ProjectRoles.Owner, ct);

                if (currentOwner.UserId != userId)
                {
                    currentOwner.Role = ProjectRoles.Admin;
                    member.Role = ProjectRoles.Owner;
                    var project = await dbContext.Projects.SingleAsync(x => x.Id == projectId, ct);
                    project.OwnerUserId = userId;
                }
            }
            else
            {
                if (member.Role == ProjectRoles.Owner)
                {
                    throw new ApiErrorException(
                        StatusCodes.Status409Conflict,
                        ApiErrorCodes.Conflict,
                        "В проекте должен оставаться ровно один owner. Сначала назначьте нового owner.");
                }

                member.Role = request.Role;
            }

            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
        },
        cancellationToken);
});

external.MapDelete("/projects/{projectId:guid}/members/{userId:guid}", async (
    HttpContext httpContext,
    Guid projectId,
    Guid userId,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectMembersRemove, cancellationToken);

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:removeMember:{projectId}:{userId}",
        idempotencyKey,
        async ct =>
        {
            var member = await dbContext.ProjectMembers
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.UserId == userId, ct);

            if (member is null)
            {
                return new IdempotentExecutionResult(
                    StatusCodes.Status200OK,
                    new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
            }

            if (member.Role == ProjectRoles.Owner)
            {
                throw new ApiErrorException(
                    StatusCodes.Status409Conflict,
                    ApiErrorCodes.Conflict,
                    "Нельзя удалить owner проекта.");
            }

            dbContext.ProjectMembers.Remove(member);
            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
        },
        cancellationToken);
});

external.MapGet("/projects/{projectId:guid}/accounts", async (
    HttpContext httpContext,
    Guid projectId,
    CoreDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectAccountsView, cancellationToken);

    var items = await dbContext.Accounts
        .Where(x => x.ProjectId == projectId)
        .OrderBy(x => x.DisplayName)
        .Select(x => new AccountDto(
            x.Id,
            x.ProjectId,
            x.Platform,
            x.DisplayName,
            x.BusinessStatus,
            x.ProxyConfigured,
            x.ProxyHostMasked,
            x.ProxyLoginMasked))
        .ToListAsync(cancellationToken);

    return Results.Ok(new AccountListResponse(httpContext.GetOrCreateRequestId(), items));
});

external.MapPost("/projects/{projectId:guid}/accounts", (HttpContext context) => ThrowFeatureNotReadyWithIdempotency(context, "createAccount"));
external.MapPatch("/projects/{projectId:guid}/accounts/{accountId:guid}", (HttpContext context) => ThrowFeatureNotReadyWithIdempotency(context, "updateAccount"));
external.MapDelete("/projects/{projectId:guid}/accounts/{accountId:guid}", (HttpContext context) => ThrowFeatureNotReadyWithIdempotency(context, "deleteAccount"));
external.MapGet("/projects/{projectId:guid}/accounts/{accountId:guid}/proxy-credentials", (HttpContext context) => ThrowFeatureNotReady(context, "getAccountProxyCredentialsMasked"));
external.MapPatch("/projects/{projectId:guid}/accounts/{accountId:guid}/proxy-credentials", (HttpContext context) => ThrowFeatureNotReadyWithIdempotency(context, "updateAccountProxyCredentials"));
external.MapPost("/projects/{projectId:guid}/accounts/{accountId:guid}/proxy-credentials/reveal", (HttpContext context) => ThrowFeatureNotReadyWithIdempotency(context, "revealAccountProxyCredentials"));
external.MapPost("/projects/{projectId:guid}/billing/payments", (HttpContext context) => ThrowFeatureNotReadyWithIdempotency(context, "createPayment"));
external.MapPost("/projects/{projectId:guid}/billing/addons/{addonId}/purchase", (HttpContext context) => ThrowFeatureNotReadyWithIdempotency(context, "purchaseAddon"));
external.MapPost("/projects/{projectId:guid}/billing/subscription/change-plan", (HttpContext context) => ThrowFeatureNotReadyWithIdempotency(context, "changePlan"));
external.MapPost("/account-api/{routeKey}/{action}", (HttpContext context) => ThrowFeatureNotReadyWithIdempotency(context, "proxyAccountApiAction"));

app.Run();

return;

static Guid GetCurrentUserId(HttpContext httpContext)
{
    var value = httpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

    if (!Guid.TryParse(value, out var userId))
    {
        throw new ApiErrorException(StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthorized, "Требуется валидный userId в JWT (sub).");
    }

    return userId;
}

static async Task EnsureProjectMembershipAsync(CoreDbContext dbContext, Guid projectId, Guid userId, CancellationToken cancellationToken)
{
    var exists = await dbContext.ProjectMembers.AnyAsync(
        x => x.ProjectId == projectId && x.UserId == userId,
        cancellationToken);

    if (!exists)
    {
        throw new ApiErrorException(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "Пользователь не состоит в проекте.");
    }
}

static async Task<string> RequireProjectRoleAsync(CoreDbContext dbContext, Guid projectId, Guid userId, CancellationToken cancellationToken)
{
    var role = await dbContext.ProjectMembers
        .Where(x => x.ProjectId == projectId && x.UserId == userId)
        .Select(x => x.Role)
        .SingleOrDefaultAsync(cancellationToken);

    if (string.IsNullOrWhiteSpace(role))
    {
        throw new ApiErrorException(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "Пользователь не состоит в проекте.");
    }

    return role;
}

static async Task EnsurePermissionAsync(CoreDbContext dbContext, Guid projectId, Guid userId, string permission, CancellationToken cancellationToken)
{
    var role = await RequireProjectRoleAsync(dbContext, projectId, userId, cancellationToken);
    if (!PermissionMatrix.HasPermission(role, permission))
    {
        throw new ApiErrorException(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "Недостаточно прав для выполнения операции.");
    }
}

static Guid ReadGuid(Dictionary<string, JsonElement> payload, string key)
{
    if (!payload.TryGetValue(key, out var value) || !Guid.TryParse(value.GetString(), out var parsed))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, $"Поле {key} должно быть GUID.");
    }

    return parsed;
}

static string ReadString(Dictionary<string, JsonElement> payload, string key)
{
    if (!payload.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value.GetString()))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, $"Поле {key} обязательно.");
    }

    return value.GetString()!.Trim();
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

public sealed record ProjectCreateRequest(string Name);

public sealed record RoleChangeRequest(string Role);

public sealed record ProjectDto(Guid Id, string Name, string Status);

public sealed record ProjectResponse(string RequestId, ProjectDto Project);

public sealed record ProjectListResponse(string RequestId, IReadOnlyCollection<ProjectDto> Items);

public sealed record AccountDto(
    Guid Id,
    Guid ProjectId,
    string Platform,
    string DisplayName,
    string BusinessStatus,
    bool ProxyConfigured,
    string? ProxyHostMasked,
    string? ProxyLoginMasked);

public sealed record AccountListResponse(string RequestId, IReadOnlyCollection<AccountDto> Items);

public partial class Program;
