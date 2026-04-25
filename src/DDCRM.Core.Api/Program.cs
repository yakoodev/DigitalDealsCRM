using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DDCRM.Core.Api.AccountsManager;
using DDCRM.Core.Api.Billing;
using DDCRM.Core.Api.GatewayProxy;
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
using Microsoft.Extensions.Options;

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
builder.Services.Configure<BillingClientOptions>(builder.Configuration.GetSection(BillingClientOptions.SectionName));
builder.Services.PostConfigure<BillingClientOptions>(options =>
{
    options.ServiceToken ??= builder.Configuration["INTERNAL_API_SERVICE_AUTH_CLIENT_TOKEN"];
});

builder.Services.AddHttpClient<IBillingClient, BillingHttpClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<BillingClientOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});

builder.Services.Configure<AccountsManagerClientOptions>(builder.Configuration.GetSection(AccountsManagerClientOptions.SectionName));
builder.Services.PostConfigure<AccountsManagerClientOptions>(options =>
{
    options.ServiceToken ??= builder.Configuration["INTERNAL_API_SERVICE_AUTH_CLIENT_TOKEN"];
});

builder.Services.AddHttpClient<IAccountsManagerClient, AccountsManagerHttpClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<AccountsManagerClientOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});

builder.Services.Configure<GatewayProxyClientOptions>(builder.Configuration.GetSection(GatewayProxyClientOptions.SectionName));
builder.Services.AddHttpClient<IGatewayProxyClient, GatewayProxyHttpClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<GatewayProxyClientOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});

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

app.MapGet("/health", (HttpContext httpContext) =>
    Results.Ok(new GenericObjectResponse(
        httpContext.GetOrCreateRequestId(),
        new Dictionary<string, object?>
        {
            ["status"] = "ok",
        })));

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
            (member, project) => new
            {
                project.Id,
                project.Name,
                project.Status,
            })
        .OrderBy(x => x.Name)
        .Select(x => new ProjectDto(x.Id, x.Name, x.Status))
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

external.MapPost("/projects/{projectId:guid}/accounts", async (
    HttpContext httpContext,
    Guid projectId,
    Dictionary<string, JsonElement> request,
    CoreDbContext dbContext,
    IAccountsManagerClient accountsManagerClient,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectAccountsLifecycleManage, cancellationToken);

    var platform = ReadString(request, "platform");
    var displayName = ReadString(request, "displayName");
    var proxyConfig = ReadProxyConfig(request, "proxyConfig", required: true)!;
    var accountId = CreateDeterministicGuid($"core:createAccount:{projectId}:{idempotencyKey}");

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:createAccount:{projectId}",
        idempotencyKey,
        async ct =>
        {
            var existing = await dbContext.Accounts.SingleOrDefaultAsync(
                x => x.ProjectId == projectId && x.Id == accountId,
                ct);

            if (existing is null)
            {
                await accountsManagerClient.CreateLifecycleAsync(
                    projectId,
                    accountId,
                    platform,
                    ToProxyConfigDictionary(proxyConfig),
                    idempotencyKey,
                    ct);

                existing = new AccountEntity
                {
                    Id = accountId,
                    ProjectId = projectId,
                    Platform = platform,
                    DisplayName = displayName,
                    BusinessStatus = "active",
                    ProxyConfigured = true,
                    ProxyHostMasked = MaskSensitive(proxyConfig.Host),
                    ProxyLoginMasked = MaskSensitive(proxyConfig.Login),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                };

                dbContext.Accounts.Add(existing);
                await dbContext.SaveChangesAsync(ct);
            }

            return new IdempotentExecutionResult(
                StatusCodes.Status201Created,
                new AccountResponse(httpContext.GetOrCreateRequestId(), ToAccountDto(existing)));
        },
        cancellationToken);
});

external.MapPatch("/projects/{projectId:guid}/accounts/{accountId:guid}", async (
    HttpContext httpContext,
    Guid projectId,
    Guid accountId,
    Dictionary<string, JsonElement> request,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectAccountsLifecycleManage, cancellationToken);

    if (request.ContainsKey("proxyConfig"))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Для изменения proxy credentials используйте updateAccountProxyCredentials.");
    }

    var displayName = TryReadString(request, "displayName");
    var businessStatus = TryReadString(request, "businessStatus");

    if (displayName is null && businessStatus is null)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Требуется хотя бы одно поле для обновления: displayName или businessStatus.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:updateAccount:{projectId}:{accountId}",
        idempotencyKey,
        async ct =>
        {
            var account = await dbContext.Accounts.SingleOrDefaultAsync(
                x => x.ProjectId == projectId && x.Id == accountId,
                ct);

            if (account is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Аккаунт не найден.");
            }

            if (displayName is not null)
            {
                account.DisplayName = displayName;
            }

            if (businessStatus is not null)
            {
                account.BusinessStatus = businessStatus;
            }

            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AccountResponse(httpContext.GetOrCreateRequestId(), ToAccountDto(account)));
        },
        cancellationToken);
});

external.MapDelete("/projects/{projectId:guid}/accounts/{accountId:guid}", async (
    HttpContext httpContext,
    Guid projectId,
    Guid accountId,
    CoreDbContext dbContext,
    IAccountsManagerClient accountsManagerClient,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectAccountsLifecycleManage, cancellationToken);

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:deleteAccount:{projectId}:{accountId}",
        idempotencyKey,
        async ct =>
        {
            await accountsManagerClient.DeleteLifecycleAsync(accountId, idempotencyKey, ct);

            var account = await dbContext.Accounts.SingleOrDefaultAsync(
                x => x.ProjectId == projectId && x.Id == accountId,
                ct);

            if (account is not null)
            {
                dbContext.Accounts.Remove(account);
                await dbContext.SaveChangesAsync(ct);
            }

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
        },
        cancellationToken);
});

external.MapGet("/projects/{projectId:guid}/accounts/{accountId:guid}/proxy-credentials", async (
    HttpContext httpContext,
    Guid projectId,
    Guid accountId,
    CoreDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectAccountsView, cancellationToken);

    var account = await dbContext.Accounts.SingleOrDefaultAsync(
        x => x.ProjectId == projectId && x.Id == accountId,
        cancellationToken);

    if (account is null)
    {
        throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Аккаунт не найден.");
    }

    return Results.Ok(new ProxyCredentialsMaskedResponse(
        httpContext.GetOrCreateRequestId(),
        new ProxyCredentialsMaskedDto(
            account.ProxyConfigured,
            account.ProxyHostMasked,
            account.ProxyLoginMasked)));
});

external.MapPatch("/projects/{projectId:guid}/accounts/{accountId:guid}/proxy-credentials", async (
    HttpContext httpContext,
    Guid projectId,
    Guid accountId,
    Dictionary<string, JsonElement> request,
    CoreDbContext dbContext,
    IAccountsManagerClient accountsManagerClient,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectAccountsProxyCredentialsUpdate, cancellationToken);

    var reason = ReadString(request, "reason");
    if (reason.Length < 3)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Поле reason должно быть длиной не менее 3 символов.");
    }

    var proxyConfig = ReadProxyConfig(request, "proxyConfig", required: true)!;

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:updateProxyCredentials:{projectId}:{accountId}",
        idempotencyKey,
        async ct =>
        {
            var account = await dbContext.Accounts.SingleOrDefaultAsync(
                x => x.ProjectId == projectId && x.Id == accountId,
                ct);

            if (account is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Аккаунт не найден.");
            }

            await accountsManagerClient.UpdateLifecycleAsync(
                accountId,
                ToProxyConfigDictionary(proxyConfig),
                idempotencyKey,
                ct);

            account.ProxyConfigured = true;
            account.ProxyHostMasked = MaskSensitive(proxyConfig.Host);
            account.ProxyLoginMasked = MaskSensitive(proxyConfig.Login);

            dbContext.ProxyCredentialsAudits.Add(new ProxyCredentialsAuditEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                AccountId = accountId,
                ActorUserId = actorId,
                Operation = "update",
                Reason = reason,
                RequestId = httpContext.GetOrCreateRequestId(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });

            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
        },
        cancellationToken);
});
external.MapPost("/projects/{projectId:guid}/accounts/{accountId:guid}/proxy-credentials/reveal", async (
    HttpContext httpContext,
    Guid projectId,
    Guid accountId,
    Dictionary<string, JsonElement> request,
    CoreDbContext dbContext,
    IGatewayProxyClient gatewayProxyClient,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectAccountsProxyCredentialsReveal, cancellationToken);

    var reason = ReadString(request, "reason");
    if (reason.Length < 3)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Поле reason должно быть длиной не менее 3 символов.");
    }

    var accountExists = await dbContext.Accounts.AnyAsync(
        x => x.ProjectId == projectId && x.Id == accountId,
        cancellationToken);

    if (!accountExists)
    {
        throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Аккаунт не найден.");
    }

    var authorizationHeader = httpContext.Request.Headers.Authorization.ToString();
    if (string.IsNullOrWhiteSpace(authorizationHeader))
    {
        throw new ApiErrorException(StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthorized, "Отсутствует заголовок Authorization.");
    }

    var revealPayload = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
    {
        ["accountId"] = JsonSerializer.SerializeToElement(accountId),
        ["reason"] = JsonSerializer.SerializeToElement(reason),
    };

    var revealResult = await gatewayProxyClient.InvokeAccountApiActionAsync(
        BuildRouteKey(accountId),
        "ext.account.proxy-credentials.reveal",
        revealPayload,
        authorizationHeader,
        idempotencyKey,
        cancellationToken);

    var proxyConfig = ReadProxyConfigFromRevealResult(revealResult);

    _ = await idempotency.ExecuteAsync(
        dbContext,
        $"core:revealProxyCredentials:{projectId}:{accountId}",
        idempotencyKey,
        _ =>
        {
            dbContext.ProxyCredentialsAudits.Add(new ProxyCredentialsAuditEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                AccountId = accountId,
                ActorUserId = actorId,
                Operation = "reveal",
                Reason = reason,
                RequestId = httpContext.GetOrCreateRequestId(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });

            return Task.FromResult(new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "completed")));
        },
        cancellationToken);

    return Results.Ok(new ProxyCredentialsRevealResponse(
        httpContext.GetOrCreateRequestId(),
        new ProxyConfigDto(proxyConfig.Host, proxyConfig.Port, proxyConfig.Login, proxyConfig.Password)));
});
external.MapPost("/projects/{projectId:guid}/billing/payments", async (
    HttpContext httpContext,
    Guid projectId,
    Dictionary<string, JsonElement>? request,
    CoreDbContext dbContext,
    IBillingClient billingClient,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectBillingChangePlan, cancellationToken);

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:billing:createPayment:{projectId}",
        idempotencyKey,
        async ct =>
        {
            var data = await billingClient.CreatePaymentAsync(projectId, request, idempotencyKey, ct);
            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new GenericObjectResponse(httpContext.GetOrCreateRequestId(), data));
        },
        cancellationToken);
});

external.MapPost("/projects/{projectId:guid}/billing/addons/{addonId}/purchase", async (
    HttpContext httpContext,
    Guid projectId,
    string addonId,
    Dictionary<string, JsonElement>? request,
    CoreDbContext dbContext,
    IBillingClient billingClient,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectBillingChangePlan, cancellationToken);

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:billing:purchaseAddon:{projectId}:{addonId}",
        idempotencyKey,
        async ct =>
        {
            var payload = MergePayload(request, new Dictionary<string, object?>
            {
                ["addonId"] = addonId,
                ["operation"] = "addon.purchase",
            });

            var data = await billingClient.CreatePaymentAsync(projectId, payload, idempotencyKey, ct);
            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new GenericObjectResponse(httpContext.GetOrCreateRequestId(), data));
        },
        cancellationToken);
});

external.MapPost("/projects/{projectId:guid}/billing/subscription/change-plan", async (
    HttpContext httpContext,
    Guid projectId,
    Dictionary<string, JsonElement> request,
    CoreDbContext dbContext,
    IBillingClient billingClient,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectBillingChangePlan, cancellationToken);

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:billing:changePlan:{projectId}",
        idempotencyKey,
        async ct =>
        {
            var status = await billingClient.ManualActivateSubscriptionAsync(projectId, request, idempotencyKey, ct);
            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), status));
        },
        cancellationToken);
});
external.MapPost("/account-api/{routeKey}/{action}", async (
    HttpContext httpContext,
    string routeKey,
    string action,
    Dictionary<string, JsonElement>? request,
    CoreDbContext dbContext,
    IGatewayProxyClient gatewayProxyClient,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    if (string.IsNullOrWhiteSpace(routeKey) || string.IsNullOrWhiteSpace(action))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "routeKey и action обязательны.");
    }

    if (action.StartsWith("ext.account.proxy-credentials.", StringComparison.Ordinal)
        || action.StartsWith("ext.account.lifecycle.", StringComparison.Ordinal))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Для ext.account.* используйте профильные account endpoint-ы Core API.");
    }

    var authorizationHeader = httpContext.Request.Headers.Authorization.ToString();
    if (string.IsNullOrWhiteSpace(authorizationHeader))
    {
        throw new ApiErrorException(StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthorized, "Отсутствует заголовок Authorization.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:proxyAccountApiAction:{actorId}:{routeKey}:{action}",
        idempotencyKey,
        async ct =>
        {
            var result = await gatewayProxyClient.InvokeAccountApiActionAsync(
                routeKey,
                action,
                request,
                authorizationHeader,
                idempotencyKey,
                ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new ProxyResponse(httpContext.GetOrCreateRequestId(), result));
        },
        cancellationToken);
});

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

static string? TryReadString(Dictionary<string, JsonElement> payload, string key)
{
    if (!payload.TryGetValue(key, out var value))
    {
        return null;
    }

    if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            $"Поле {key} должно быть непустой строкой.");
    }

    return value.GetString()!.Trim();
}

static ProxyConfigPayload? ReadProxyConfig(Dictionary<string, JsonElement> payload, string key, bool required)
{
    if (!payload.TryGetValue(key, out var value))
    {
        if (!required)
        {
            return null;
        }

        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, $"Поле {key} обязательно.");
    }

    if (value.ValueKind != JsonValueKind.Object)
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, $"Поле {key} должно быть объектом.");
    }

    var objectValue = value.EnumerateObject()
        .ToDictionary(x => x.Name, x => x.Value.Clone(), StringComparer.Ordinal);

    var host = ReadString(objectValue, "host");
    var login = ReadString(objectValue, "login");
    var password = ReadString(objectValue, "password");

    if (!objectValue.TryGetValue("port", out var portValue))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Поле proxyConfig.port обязательно.");
    }

    var port = portValue.ValueKind switch
    {
        JsonValueKind.Number when portValue.TryGetInt32(out var intPort) => intPort,
        JsonValueKind.String when int.TryParse(portValue.GetString(), out var stringPort) => stringPort,
        _ => throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Поле proxyConfig.port должно быть числом."),
    };

    if (port is < 1 or > 65535)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Поле proxyConfig.port должно быть в диапазоне 1..65535.");
    }

    return new ProxyConfigPayload(host, port, login, password);
}

static Dictionary<string, object?> ToProxyConfigDictionary(ProxyConfigPayload proxyConfig)
{
    return new Dictionary<string, object?>
    {
        ["host"] = proxyConfig.Host,
        ["port"] = proxyConfig.Port,
        ["login"] = proxyConfig.Login,
        ["password"] = proxyConfig.Password,
    };
}

static string BuildRouteKey(Guid accountId) => $"rk.{accountId:N}";

static ProxyConfigPayload ReadProxyConfigFromRevealResult(JsonElement revealResult)
{
    if (!revealResult.TryGetProperty("proxyConfig", out var proxyConfigElement))
    {
        throw new ApiErrorException(
            StatusCodes.Status502BadGateway,
            ApiErrorCodes.InternalError,
            "Gateway вернул reveal-ответ без proxyConfig.");
    }

    var payload = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
    {
        ["proxyConfig"] = proxyConfigElement.Clone(),
    };

    return ReadProxyConfig(payload, "proxyConfig", required: true)!;
}

static string MaskSensitive(string value)
{
    if (value.Length <= 2)
    {
        return "***";
    }

    if (value.Length <= 6)
    {
        return $"{value[0]}***";
    }

    return $"{value[..2]}***{value[^2..]}";
}

static AccountDto ToAccountDto(AccountEntity account)
{
    return new AccountDto(
        account.Id,
        account.ProjectId,
        account.Platform,
        account.DisplayName,
        account.BusinessStatus,
        account.ProxyConfigured,
        account.ProxyHostMasked,
        account.ProxyLoginMasked);
}

static Guid CreateDeterministicGuid(string input)
{
    var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
    return new Guid(hash);
}

static Dictionary<string, JsonElement> MergePayload(
    IDictionary<string, JsonElement>? payload,
    IDictionary<string, object?> additions)
{
    var merged = payload is null
        ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        : payload.ToDictionary(x => x.Key, x => x.Value.Clone(), StringComparer.Ordinal);

    foreach (var (key, value) in additions)
    {
        merged[key] = JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object));
    }

    return merged;
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

public sealed record AccountResponse(string RequestId, AccountDto Account);

public sealed record ProxyCredentialsMaskedDto(bool Configured, string? HostMasked, string? LoginMasked);

public sealed record ProxyCredentialsMaskedResponse(string RequestId, ProxyCredentialsMaskedDto ProxyCredentials);

public sealed record ProxyConfigDto(string Host, int Port, string Login, string Password);

public sealed record ProxyCredentialsRevealResponse(string RequestId, ProxyConfigDto ProxyConfig);

public sealed record ProxyResponse(string RequestId, JsonElement Result);

public sealed record GenericObjectResponse(string RequestId, IDictionary<string, object?> Data);

internal sealed record ProxyConfigPayload(string Host, int Port, string Login, string Password);

public partial class Program;
