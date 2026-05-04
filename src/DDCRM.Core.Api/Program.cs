using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DDCRM.Core.Api.AccountsManager;
using DDCRM.Core.Api.Billing;
using DDCRM.Core.Api.GatewayProxy;
using DDCRM.Core.Api.Integrations;
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
builder.Services.Configure<FunPayStatClientOptions>(builder.Configuration.GetSection(FunPayStatClientOptions.SectionName));
builder.Services.PostConfigure<FunPayStatClientOptions>(options =>
{
    options.ServiceToken ??= builder.Configuration["FUNPAYSTAT_INTEGRATION_SERVICE_TOKEN"];
});
builder.Services.Configure<SteamAccountsManagerClientOptions>(builder.Configuration.GetSection(SteamAccountsManagerClientOptions.SectionName));
builder.Services.PostConfigure<SteamAccountsManagerClientOptions>(options =>
{
    options.ServiceToken ??= builder.Configuration["STEAM_ACCOUNTS_MANAGER_INTEGRATION_SERVICE_TOKEN"];
});
builder.Services.Configure<TelegramNotificationOptions>(builder.Configuration.GetSection(TelegramNotificationOptions.SectionName));
builder.Services.PostConfigure<TelegramNotificationOptions>(options =>
{
    options.BotToken ??= builder.Configuration["TELEGRAM_NOTIFICATION_BOT_TOKEN"];
    options.LinkWebhookSecret ??= builder.Configuration["TELEGRAM_LINK_WEBHOOK_SECRET"];
});
builder.Services.Configure<EntitlementCheckClientOptions>(builder.Configuration.GetSection(EntitlementCheckClientOptions.SectionName));
builder.Services.PostConfigure<EntitlementCheckClientOptions>(options =>
{
    options.ServiceToken ??= builder.Configuration["INTERNAL_API_SERVICE_AUTH_CLIENT_TOKEN"];
});
builder.Services.AddHttpClient<FunPayStatIntegrationClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<FunPayStatClientOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});
builder.Services.AddHttpClient<SteamAccountsManagerIntegrationClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<SteamAccountsManagerClientOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});
builder.Services.AddHttpClient<IEntitlementCheckClient, EntitlementCheckHttpClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<EntitlementCheckClientOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});
builder.Services.AddScoped<IProjectServiceIntegrationClient>(serviceProvider => serviceProvider.GetRequiredService<FunPayStatIntegrationClient>());
builder.Services.AddScoped<IProjectServiceIntegrationClient>(serviceProvider => serviceProvider.GetRequiredService<SteamAccountsManagerIntegrationClient>());
builder.Services.AddScoped<ProjectServiceIntegrationRegistry>();
builder.Services.AddSingleton<ProjectSecretCrypto>();
builder.Services.AddScoped<TelegramNotificationSender>();
builder.Services.AddHostedService<ServiceCredentialSyncBackgroundService>();
builder.Services.AddHostedService<NotificationOutboxBackgroundService>();

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

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                context.HttpContext.Items["jwt-auth-failure"] = context.Exception.GetType().Name;
                return Task.CompletedTask;
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();

                if (context.Response.HasStarted)
                {
                    return;
                }

                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";

                var requestId = context.HttpContext.GetOrCreateRequestId();
                var hasBearerHeader = context.Request.Headers.TryGetValue(
                    "Authorization",
                    out var authHeader)
                    && authHeader.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
                var authFailure = context.HttpContext.Items.TryGetValue("jwt-auth-failure", out var failure)
                    ? failure?.ToString()
                    : null;
                var message = hasBearerHeader
                    ? "Требуется валидный bearer JWT."
                    : "Отсутствует bearer JWT в заголовке Authorization.";
                var details = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["hasBearerHeader"] = hasBearerHeader,
                };
                if (!string.IsNullOrWhiteSpace(authFailure))
                {
                    details["authFailure"] = authFailure;
                }

                var payload = new ErrorResponse(
                    ApiErrorCodes.Unauthorized,
                    message,
                    requestId,
                    details);

                await context.Response.WriteAsJsonAsync(payload);
            },
            OnForbidden = async context =>
            {
                if (context.Response.HasStarted)
                {
                    return;
                }

                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";

                var requestId = context.HttpContext.GetOrCreateRequestId();
                var payload = new ErrorResponse(
                    ApiErrorCodes.Forbidden,
                    "Недостаточно прав для выполнения операции.",
                    requestId);

                await context.Response.WriteAsJsonAsync(payload);
            },
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

var systemPermissionClaimType =
    builder.Configuration["EXTERNAL_API_SYSTEM_PERMISSION_CLAIM_TYPE"]
    ?? "ddcrm.system.permissions";
var systemPermissionClaimValue =
    builder.Configuration["EXTERNAL_API_SYSTEM_PERMISSION_CLAIM_VALUE"]
    ?? "system.accountManager.manage";
var systemIntegrationsPermissionValue =
    builder.Configuration["EXTERNAL_API_SYSTEM_INTEGRATIONS_PERMISSION_CLAIM_VALUE"]
    ?? "system.integrations.manage";

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

external.MapGet("/projects/{projectId:guid}/account-types", async (
    HttpContext httpContext,
    Guid projectId,
    CoreDbContext dbContext,
    IAccountsManagerClient accountsManagerClient,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectAccountsLifecycleManage, cancellationToken);

    var platformGrants = await dbContext.ProjectIntegrationGrants
        .AsNoTracking()
        .Where(x => x.ProjectId == projectId && x.Status == "active" && x.IntegrationKey.StartsWith("platform."))
        .Select(x => x.IntegrationKey)
        .ToListAsync(cancellationToken);
    var allowedPlatforms = platformGrants
        .Select(key => key["platform.".Length..])
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var accountTypes = await accountsManagerClient.ListAccountTypesAsync(cancellationToken);
    var items = accountTypes
        .Where(x => x.Enabled && allowedPlatforms.Contains(x.Platform))
        .OrderBy(x => x.SortOrder)
        .ThenBy(x => x.DisplayName)
        .Select(ToAccountTypeDto)
        .ToList();

    return Results.Ok(new AccountTypeListResponse(httpContext.GetOrCreateRequestId(), items));
});

external.MapGet("/admin/account-manager/worker-servers", async (
    HttpContext httpContext,
    IAccountsManagerClient accountsManagerClient,
    CancellationToken cancellationToken) =>
{
    EnsureSystemPermission(httpContext, systemPermissionClaimType, systemPermissionClaimValue);

    var workerServers = await accountsManagerClient.ListWorkerServersAsync(cancellationToken);
    var items = workerServers
        .OrderBy(x => x.ServerId, StringComparer.Ordinal)
        .Select(ToAdminWorkerServerDto)
        .ToList();

    return Results.Ok(new AdminWorkerServerListResponse(httpContext.GetOrCreateRequestId(), items));
});

external.MapPut("/admin/account-manager/worker-servers/{serverId}", async (
    HttpContext httpContext,
    string serverId,
    AdminWorkerServerUpsertRequest request,
    IAccountsManagerClient accountsManagerClient,
    CancellationToken cancellationToken) =>
{
    EnsureSystemPermission(httpContext, systemPermissionClaimType, systemPermissionClaimValue);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    var input = new AccountsManagerWorkerServerUpsertInput(
        request.BaseUrlTemplate,
        request.Status,
        request.Capacity,
        request.CurrentLoad,
        request.Health,
        request.DockerHost,
        request.DockerNetwork,
        request.Registry is null
            ? null
            : new AccountsManagerWorkerServerRegistryUpsertInput(
                request.Registry.Enabled,
                request.Registry.Host,
                request.Registry.Username,
                request.Registry.Token,
                request.Registry.ClearToken),
        request.Metadata);
    var upserted = await accountsManagerClient.UpsertWorkerServerAsync(
        serverId,
        input,
        idempotencyKey,
        cancellationToken);

    return Results.Ok(new AdminWorkerServerResponse(
        httpContext.GetOrCreateRequestId(),
        ToAdminWorkerServerDto(upserted)));
});

external.MapGet("/admin/account-manager/account-types", async (
    HttpContext httpContext,
    IAccountsManagerClient accountsManagerClient,
    CancellationToken cancellationToken) =>
{
    EnsureSystemPermission(httpContext, systemPermissionClaimType, systemPermissionClaimValue);

    var accountTypes = await accountsManagerClient.ListAccountTypesAsync(cancellationToken);
    var items = accountTypes
        .OrderBy(x => x.Platform, StringComparer.Ordinal)
        .ThenBy(x => x.SortOrder)
        .Select(ToAdminAccountTypeDto)
        .ToList();

    return Results.Ok(new AdminAccountTypeListResponse(httpContext.GetOrCreateRequestId(), items));
});

external.MapPut("/admin/account-manager/account-types/{accountTypeId}", async (
    HttpContext httpContext,
    string accountTypeId,
    AdminAccountTypeUpsertRequest request,
    IAccountsManagerClient accountsManagerClient,
    CancellationToken cancellationToken) =>
{
    EnsureSystemPermission(httpContext, systemPermissionClaimType, systemPermissionClaimValue);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    var runtime = request.Runtime is null
        ? null
        : new AccountsManagerAccountTypeRuntime(
            request.Runtime.AutospawnEnabled,
            request.Runtime.WorkerImage,
            request.Runtime.WorkerPathPrefix,
            request.Runtime.HealthPath,
            request.Runtime.ContainerPort,
            request.Runtime.EnvironmentVariables,
            request.Runtime.WorkerCommand?.ToList());
    var formFields = request.FormFields?.Select(field => new AccountsManagerAccountTypeField(
        field.Key,
        field.Label,
        field.InputType,
        field.Required,
        field.Secret,
        field.Placeholder,
        field.DefaultValue)).ToList();
    var input = new AccountsManagerAccountTypeUpsertInput(
        request.Platform,
        request.DisplayName,
        request.Description,
        request.WorkerProfileId,
        request.Enabled,
        request.SortOrder,
        formFields,
        runtime);
    var upserted = await accountsManagerClient.UpsertAccountTypeAsync(
        accountTypeId,
        input,
        idempotencyKey,
        cancellationToken);

    return Results.Ok(new AdminAccountTypeResponse(
        httpContext.GetOrCreateRequestId(),
        ToAdminAccountTypeDto(upserted)));
});
external.MapGet("/admin/integrations/projects/{projectId:guid}/grants", async (
    HttpContext httpContext,
    Guid projectId,
    CoreDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    EnsureSystemPermission(httpContext, systemPermissionClaimType, systemIntegrationsPermissionValue);

    var grants = await dbContext.ProjectIntegrationGrants
        .AsNoTracking()
        .Where(x => x.ProjectId == projectId)
        .OrderBy(x => x.IntegrationKey)
        .ToListAsync(cancellationToken);

    var credentials = await dbContext.ProjectServiceCredentials
        .AsNoTracking()
        .Where(x => x.ProjectId == projectId)
        .ToDictionaryAsync(x => x.IntegrationKey, StringComparer.OrdinalIgnoreCase, cancellationToken);

    var items = grants.Select(grant =>
    {
        credentials.TryGetValue(grant.IntegrationKey, out var credential);
        return new AdminIntegrationGrantDto(
            grant.IntegrationKey,
            grant.Status,
            SplitScopes(grant.ScopesCsv),
            grant.GrantedAtUtc,
            grant.RevokedAtUtc,
            credential?.Status,
            credential?.SecretMasked);
    }).ToList();

    return Results.Ok(new AdminIntegrationGrantListResponse(httpContext.GetOrCreateRequestId(), items));
});

external.MapPut("/admin/integrations/projects/{projectId:guid}/grants/{integrationKey}", async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    AdminIntegrationGrantUpsertRequest request,
    CoreDbContext dbContext,
    ProjectSecretCrypto crypto,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    EnsureSystemPermission(httpContext, systemPermissionClaimType, systemIntegrationsPermissionValue);
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    var normalizedIntegrationKey = NormalizeIntegrationKey(integrationKey);
    if (!IsSupportedIntegrationKey(normalizedIntegrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Неподдерживаемый integration key.");
    }

    var scopes = NormalizeScopes(normalizedIntegrationKey, request.Scopes);

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:integrationGrantUpsert:{projectId}:{normalizedIntegrationKey}",
        idempotencyKey,
        async ct =>
        {
            var projectExists = await dbContext.Projects.AnyAsync(x => x.Id == projectId, ct);
            if (!projectExists)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Проект не найден.");
            }

            var grant = await dbContext.ProjectIntegrationGrants
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IntegrationKey == normalizedIntegrationKey, ct);

            var now = DateTimeOffset.UtcNow;
            if (grant is null)
            {
                grant = new ProjectIntegrationGrantEntity
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    IntegrationKey = normalizedIntegrationKey,
                    Status = "active",
                    ScopesCsv = string.Join(',', scopes),
                    GrantedByUserId = actorId,
                    GrantedAtUtc = now,
                };
                dbContext.ProjectIntegrationGrants.Add(grant);
            }
            else
            {
                grant.Status = "active";
                grant.ScopesCsv = string.Join(',', scopes);
                grant.GrantedByUserId = actorId;
                grant.GrantedAtUtc = now;
                grant.RevokedByUserId = null;
                grant.RevokedAtUtc = null;
            }

            ProjectServiceCredentialEntity? credential = null;
            if (IntegrationKeys.ServiceIntegrations.Contains(normalizedIntegrationKey))
            {
                var token = GenerateProjectServiceToken(normalizedIntegrationKey);
                credential = await dbContext.ProjectServiceCredentials
                    .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IntegrationKey == normalizedIntegrationKey, ct);

                if (credential is null)
                {
                    credential = new ProjectServiceCredentialEntity
                    {
                        Id = Guid.NewGuid(),
                        ProjectId = projectId,
                        IntegrationKey = normalizedIntegrationKey,
                        ScopesCsv = string.Join(',', scopes),
                        Status = "pending_sync",
                        SecretCiphertext = crypto.Encrypt(token),
                        SecretHashSha256 = ProjectSecretCrypto.ComputeSha256(token),
                        SecretMasked = MaskToken(token),
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now,
                    };
                    dbContext.ProjectServiceCredentials.Add(credential);
                }
                else
                {
                    credential.ScopesCsv = string.Join(',', scopes);
                    credential.Status = "pending_sync";
                    credential.SecretCiphertext = crypto.Encrypt(token);
                    credential.SecretHashSha256 = ProjectSecretCrypto.ComputeSha256(token);
                    credential.SecretMasked = MaskToken(token);
                    credential.UpdatedAtUtc = now;
                    credential.RevokedAtUtc = null;
                }

                dbContext.ServiceCredentialSyncOutbox.Add(new ServiceCredentialSyncOutboxEntity
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    CredentialId = credential.Id,
                    IntegrationKey = normalizedIntegrationKey,
                    Operation = "grant",
                    Status = "pending",
                    AttemptCount = 0,
                    NextAttemptAtUtc = now,
                    CreatedAtUtc = now,
                });
            }

            dbContext.NotificationOutbox.Add(CreateNotificationOutbox(
                projectId,
                "integration.granted",
                $"Интеграция `{normalizedIntegrationKey}` выдана проекту. Scope: {string.Join(", ", scopes)}",
                $"{projectId:N}:{normalizedIntegrationKey}:grant:{idempotencyKey}"));

            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AdminIntegrationGrantResponse(
                    httpContext.GetOrCreateRequestId(),
                    new AdminIntegrationGrantDto(
                        normalizedIntegrationKey,
                        grant.Status,
                        scopes,
                        grant.GrantedAtUtc,
                        grant.RevokedAtUtc,
                        credential?.Status,
                        credential?.SecretMasked)));
        },
        cancellationToken);
});

external.MapDelete("/admin/integrations/projects/{projectId:guid}/grants/{integrationKey}", async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    EnsureSystemPermission(httpContext, systemPermissionClaimType, systemIntegrationsPermissionValue);
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    var normalizedIntegrationKey = NormalizeIntegrationKey(integrationKey);
    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:integrationGrantRevoke:{projectId}:{normalizedIntegrationKey}",
        idempotencyKey,
        async ct =>
        {
            var grant = await dbContext.ProjectIntegrationGrants
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IntegrationKey == normalizedIntegrationKey, ct);

            if (grant is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Grant не найден.");
            }

            var now = DateTimeOffset.UtcNow;
            grant.Status = "revoked";
            grant.RevokedByUserId = actorId;
            grant.RevokedAtUtc = now;

            var credential = await dbContext.ProjectServiceCredentials
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IntegrationKey == normalizedIntegrationKey, ct);

            if (credential is not null)
            {
                credential.Status = "revoking";
                credential.UpdatedAtUtc = now;

                dbContext.ServiceCredentialSyncOutbox.Add(new ServiceCredentialSyncOutboxEntity
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    CredentialId = credential.Id,
                    IntegrationKey = normalizedIntegrationKey,
                    Operation = "revoke",
                    Status = "pending",
                    AttemptCount = 0,
                    NextAttemptAtUtc = now,
                    CreatedAtUtc = now,
                });
            }

            dbContext.NotificationOutbox.Add(CreateNotificationOutbox(
                projectId,
                "integration.revoked",
                $"Интеграция `{normalizedIntegrationKey}` отозвана у проекта.",
                $"{projectId:N}:{normalizedIntegrationKey}:revoke:{idempotencyKey}"));

            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
        },
        cancellationToken);
});

external.MapGet("/admin/integrations/telegram/proxies", async (
    HttpContext httpContext,
    CoreDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    EnsureSystemPermission(httpContext, systemPermissionClaimType, systemIntegrationsPermissionValue);

    var items = await dbContext.TelegramProxyProfiles
        .AsNoTracking()
        .OrderByDescending(x => x.IsActive)
        .ThenBy(x => x.Name)
        .Select(x => new AdminTelegramProxyProfileDto(
            x.Id,
            x.Name,
            x.ProxyType,
            x.Host,
            x.Port,
            x.IsActive,
            !string.IsNullOrWhiteSpace(x.LoginCiphertext) || !string.IsNullOrWhiteSpace(x.PasswordCiphertext),
            x.UpdatedAtUtc))
        .ToListAsync(cancellationToken);

    return Results.Ok(new AdminTelegramProxyProfileListResponse(httpContext.GetOrCreateRequestId(), items));
});

external.MapPut("/admin/integrations/telegram/proxies/{proxyId:guid}", async (
    HttpContext httpContext,
    Guid proxyId,
    AdminTelegramProxyProfileUpsertRequest request,
    CoreDbContext dbContext,
    ProjectSecretCrypto crypto,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    EnsureSystemPermission(httpContext, systemPermissionClaimType, systemIntegrationsPermissionValue);
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Host))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "name и host обязательны.");
    }

    if (request.Port is < 1 or > 65535)
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "port должен быть в диапазоне 1..65535.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:telegramProxyUpsert:{proxyId}",
        idempotencyKey,
        async ct =>
        {
            var entity = await dbContext.TelegramProxyProfiles.SingleOrDefaultAsync(x => x.Id == proxyId, ct);
            var now = DateTimeOffset.UtcNow;

            if (request.SetActive)
            {
                var activeProfiles = await dbContext.TelegramProxyProfiles.Where(x => x.IsActive).ToListAsync(ct);
                foreach (var profile in activeProfiles)
                {
                    profile.IsActive = false;
                    profile.UpdatedAtUtc = now;
                    profile.UpdatedByUserId = actorId;
                }
            }

            if (entity is null)
            {
                entity = new TelegramProxyProfileEntity
                {
                    Id = proxyId,
                    Name = request.Name.Trim(),
                    ProxyType = NormalizeProxyType(request.ProxyType),
                    Host = request.Host.Trim(),
                    Port = request.Port,
                    LoginCiphertext = string.IsNullOrWhiteSpace(request.Login) ? null : crypto.Encrypt(request.Login.Trim()),
                    PasswordCiphertext = string.IsNullOrWhiteSpace(request.Password) ? null : crypto.Encrypt(request.Password.Trim()),
                    IsActive = request.SetActive,
                    UpdatedByUserId = actorId,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                dbContext.TelegramProxyProfiles.Add(entity);
            }
            else
            {
                entity.Name = request.Name.Trim();
                entity.ProxyType = NormalizeProxyType(request.ProxyType);
                entity.Host = request.Host.Trim();
                entity.Port = request.Port;
                if (request.ClearCredentials)
                {
                    entity.LoginCiphertext = null;
                    entity.PasswordCiphertext = null;
                }

                if (!string.IsNullOrWhiteSpace(request.Login))
                {
                    entity.LoginCiphertext = crypto.Encrypt(request.Login.Trim());
                }

                if (!string.IsNullOrWhiteSpace(request.Password))
                {
                    entity.PasswordCiphertext = crypto.Encrypt(request.Password.Trim());
                }

                entity.IsActive = request.SetActive || entity.IsActive;
                entity.UpdatedByUserId = actorId;
                entity.UpdatedAtUtc = now;
            }

            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AdminTelegramProxyProfileResponse(
                    httpContext.GetOrCreateRequestId(),
                    new AdminTelegramProxyProfileDto(
                        entity.Id,
                        entity.Name,
                        entity.ProxyType,
                        entity.Host,
                        entity.Port,
                        entity.IsActive,
                        !string.IsNullOrWhiteSpace(entity.LoginCiphertext) || !string.IsNullOrWhiteSpace(entity.PasswordCiphertext),
                        entity.UpdatedAtUtc)));
        },
        cancellationToken);
});

external.MapGet("/projects/{projectId:guid}/integrations/status", async (
    HttpContext httpContext,
    Guid projectId,
    CoreDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);

    var grants = await dbContext.ProjectIntegrationGrants
        .AsNoTracking()
        .Where(x => x.ProjectId == projectId)
        .OrderBy(x => x.IntegrationKey)
        .ToListAsync(cancellationToken);

    var credentials = await dbContext.ProjectServiceCredentials
        .AsNoTracking()
        .Where(x => x.ProjectId == projectId)
        .ToDictionaryAsync(x => x.IntegrationKey, StringComparer.OrdinalIgnoreCase, cancellationToken);

    var groupChats = await dbContext.TelegramChatBindings
        .AsNoTracking()
        .CountAsync(x => x.ProjectId == projectId && x.BindingType == "group", cancellationToken);
    var userChats = await dbContext.TelegramChatBindings
        .AsNoTracking()
        .CountAsync(x => x.ProjectId == projectId && x.BindingType == "user", cancellationToken);

    var items = grants.Select(grant =>
    {
        credentials.TryGetValue(grant.IntegrationKey, out var credential);
        return new ProjectIntegrationStatusDto(
            grant.IntegrationKey,
            grant.Status,
            SplitScopes(grant.ScopesCsv),
            credential?.Status,
            credential?.SecretMasked);
    }).ToList();

    return Results.Ok(new ProjectIntegrationStatusResponse(
        httpContext.GetOrCreateRequestId(),
        items,
        new TelegramBindingsSummaryDto(groupChats, userChats)));
});

external.MapPost("/projects/{projectId:guid}/integrations/{integrationKey}/actions/{scope}", async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    string scope,
    Dictionary<string, JsonElement>? request,
    CoreDbContext dbContext,
    ProjectServiceIntegrationRegistry integrationRegistry,
    ProjectSecretCrypto crypto,
    IEntitlementCheckClient entitlementCheckClient,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);

    var normalizedIntegrationKey = NormalizeIntegrationKey(integrationKey);
    var normalizedScope = NormalizeScope(scope);
    if (!IntegrationKeys.ServiceIntegrations.Contains(normalizedIntegrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Integration не поддерживает service actions.");
    }

    if (!integrationRegistry.TryGet(normalizedIntegrationKey, out var integrationClient))
    {
        throw new ApiErrorException(StatusCodes.Status503ServiceUnavailable, ApiErrorCodes.InternalError, "Integration client не зарегистрирован.");
    }

    var grant = await dbContext.ProjectIntegrationGrants
        .AsNoTracking()
        .SingleOrDefaultAsync(
            x => x.ProjectId == projectId && x.IntegrationKey == normalizedIntegrationKey,
            cancellationToken);

    if (grant is null || grant.Status != "active")
    {
        throw new ApiErrorException(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "Интеграция не выдана проекту.");
    }

    var allowedScopes = SplitScopes(grant.ScopesCsv);
    if (!allowedScopes.Contains(normalizedScope, StringComparer.OrdinalIgnoreCase))
    {
        throw new ApiErrorException(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "Scope не разрешён для проекта.");
    }

    var action = $"ext.integration.{normalizedIntegrationKey}.{normalizedScope}";
    var entitlementAllowed = await entitlementCheckClient.IsAllowedAsync(projectId, actorId, action, cancellationToken);
    if (!entitlementAllowed)
    {
        throw new ApiErrorException(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "Операция заблокирована entitlement policy.");
    }

    var credential = await dbContext.ProjectServiceCredentials
        .AsNoTracking()
        .SingleOrDefaultAsync(
            x => x.ProjectId == projectId && x.IntegrationKey == normalizedIntegrationKey,
            cancellationToken);

    if (credential is null || credential.Status != "active")
    {
        throw new ApiErrorException(StatusCodes.Status409Conflict, ApiErrorCodes.Conflict, "Сервисный токен ещё не активирован.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:integrationInvoke:{projectId}:{normalizedIntegrationKey}:{normalizedScope}",
        idempotencyKey,
        async _ =>
        {
            var payload = request is null
                ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                : request.ToDictionary(x => x.Key, x => x.Value.Clone(), StringComparer.Ordinal);

            payload["projectToken"] = JsonSerializer.SerializeToElement(crypto.Decrypt(credential.SecretCiphertext));
            var result = await integrationClient.InvokeAsync(projectId, normalizedScope, payload, idempotencyKey, cancellationToken);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new ProxyResponse(httpContext.GetOrCreateRequestId(), result));
        },
        cancellationToken);
});

external.MapPost("/projects/{projectId:guid}/integrations/telegram/link-codes", async (
    HttpContext httpContext,
    Guid projectId,
    TelegramLinkCodeCreateRequest request,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);
    await EnsureActiveIntegrationGrantAsync(dbContext, projectId, IntegrationKeys.Telegram, cancellationToken);

    var bindingType = NormalizeBindingType(request.BindingType);
    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:telegramLinkCode:{projectId}:{bindingType}",
        idempotencyKey,
        async ct =>
        {
            var code = GenerateTelegramLinkCode();
            var now = DateTimeOffset.UtcNow;
            var expiresAt = now.AddMinutes(15);

            dbContext.TelegramLinkCodes.Add(new TelegramLinkCodeEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Code = code,
                BindingType = bindingType,
                CreatedByUserId = actorId,
                CreatedAtUtc = now,
                ExpiresAtUtc = expiresAt,
            });

            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new TelegramLinkCodeResponse(httpContext.GetOrCreateRequestId(), code, bindingType, expiresAt));
        },
        cancellationToken);
});

external.MapPost("/projects/{projectId:guid}/integrations/telegram/user-bind", async (
    HttpContext httpContext,
    Guid projectId,
    TelegramUserBindRequest request,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    await EnsureProjectMembershipAsync(dbContext, projectId, actorId, cancellationToken);
    await EnsureActiveIntegrationGrantAsync(dbContext, projectId, IntegrationKeys.Telegram, cancellationToken);

    if (string.IsNullOrWhiteSpace(request.ChatId))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "chatId обязателен.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:telegramUserBind:{projectId}:{actorId}",
        idempotencyKey,
        async ct =>
        {
            var existing = await dbContext.TelegramChatBindings
                .SingleOrDefaultAsync(x =>
                    x.ProjectId == projectId
                    && x.BindingType == "user"
                    && x.UserId == actorId,
                    ct);

            if (existing is null)
            {
                dbContext.TelegramChatBindings.Add(new TelegramChatBindingEntity
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    ChatId = request.ChatId.Trim(),
                    BindingType = "user",
                    UserId = actorId,
                    ChatTitle = request.ChatTitle?.Trim(),
                    LinkedByUserId = actorId,
                    LinkedAtUtc = DateTimeOffset.UtcNow,
                });
            }
            else
            {
                existing.ChatId = request.ChatId.Trim();
                existing.ChatTitle = request.ChatTitle?.Trim();
                existing.LinkedByUserId = actorId;
                existing.LinkedAtUtc = DateTimeOffset.UtcNow;
            }

            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
        },
        cancellationToken);
});

external.MapPost("/integrations/telegram/link/confirm", async (
    HttpContext httpContext,
    TelegramLinkConfirmRequest request,
    CoreDbContext dbContext,
    IOptions<TelegramNotificationOptions> telegramOptions,
    CancellationToken cancellationToken) =>
{
    var webhookSecret = telegramOptions.Value.LinkWebhookSecret;
    if (string.IsNullOrWhiteSpace(webhookSecret)
        || !httpContext.Request.Headers.TryGetValue("X-Telegram-Link-Secret", out var receivedSecret)
        || !string.Equals(receivedSecret.ToString(), webhookSecret, StringComparison.Ordinal))
    {
        throw new ApiErrorException(StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthorized, "Некорректный telegram link secret.");
    }

    if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.ChatId))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "code и chatId обязательны.");
    }

    var code = request.Code.Trim();
    var linkCode = await dbContext.TelegramLinkCodes
        .SingleOrDefaultAsync(x => x.Code == code, cancellationToken);

    if (linkCode is null || linkCode.ConsumedAtUtc.HasValue || linkCode.ExpiresAtUtc <= DateTimeOffset.UtcNow)
    {
        throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Link code не найден или истёк.");
    }

    if (!string.Equals(linkCode.BindingType, "group", StringComparison.Ordinal))
    {
        throw new ApiErrorException(StatusCodes.Status409Conflict, ApiErrorCodes.Conflict, "Link code не предназначен для group binding.");
    }

    var binding = await dbContext.TelegramChatBindings
        .SingleOrDefaultAsync(x =>
            x.ProjectId == linkCode.ProjectId
            && x.BindingType == "group"
            && x.ChatId == request.ChatId.Trim(),
            cancellationToken);

    if (binding is null)
    {
        dbContext.TelegramChatBindings.Add(new TelegramChatBindingEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = linkCode.ProjectId,
            ChatId = request.ChatId.Trim(),
            BindingType = "group",
            UserId = null,
            ChatTitle = request.ChatTitle?.Trim(),
            LinkedByUserId = linkCode.CreatedByUserId,
            LinkedAtUtc = DateTimeOffset.UtcNow,
        });
    }
    else
    {
        binding.ChatTitle = request.ChatTitle?.Trim();
        binding.LinkedAtUtc = DateTimeOffset.UtcNow;
    }

    linkCode.ConsumedAtUtc = DateTimeOffset.UtcNow;
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
}).AllowAnonymous();

external.MapPost("/projects/{projectId:guid}/integrations/notifications/critical", async (
    HttpContext httpContext,
    Guid projectId,
    CriticalNotificationRequest request,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);
    await EnsureActiveIntegrationGrantAsync(dbContext, projectId, IntegrationKeys.Telegram, cancellationToken);

    if (string.IsNullOrWhiteSpace(request.EventType) || string.IsNullOrWhiteSpace(request.Message))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "eventType и message обязательны.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:criticalNotification:{projectId}:{request.EventType.Trim()}",
        idempotencyKey,
        async ct =>
        {
            dbContext.NotificationOutbox.Add(CreateNotificationOutbox(
                projectId,
                request.EventType.Trim(),
                request.Message.Trim(),
                $"{projectId:N}:{request.EventType.Trim()}:{idempotencyKey}"));

            await dbContext.SaveChangesAsync(ct);
            return new IdempotentExecutionResult(
                StatusCodes.Status202Accepted,
                new AckResponse(httpContext.GetOrCreateRequestId(), "accepted"));
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
    var accountTypeId = TryReadString(request, "accountTypeId");
    var displayName = ReadString(request, "displayName");
    var proxyConfig = ReadProxyConfig(request, "proxyConfig", required: true)!;
    var marketplaceAuth = ReadMarketplaceAuth(request, "marketplaceAuth");
    await EnsureActiveIntegrationGrantAsync(
        dbContext,
        projectId,
        BuildPlatformIntegrationKey(platform),
        cancellationToken);

    if (accountTypeId is not null)
    {
        var accountTypes = await accountsManagerClient.ListAccountTypesAsync(cancellationToken);
        var accountType = accountTypes.SingleOrDefault(x =>
            x.Enabled
            && string.Equals(x.AccountTypeId, accountTypeId, StringComparison.OrdinalIgnoreCase));

        if (accountType is null)
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "Указанный accountTypeId недоступен для создания аккаунта.");
        }

        if (!string.Equals(platform, accountType.Platform, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "platform должен соответствовать выбранному accountTypeId.");
        }

        platform = accountType.Platform;
    }

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
                    marketplaceAuth is null ? null : ToAccountsManagerMarketplaceAuth(marketplaceAuth),
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
        || action.StartsWith("ext.account.lifecycle.", StringComparison.Ordinal)
        || action.StartsWith("ext.account.marketplace-auth.", StringComparison.Ordinal))
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

static void EnsureSystemPermission(HttpContext httpContext, string claimType, string requiredPermission)
{
    if (HasSystemPermission(httpContext.User, claimType, requiredPermission))
    {
        return;
    }

    throw new ApiErrorException(
        StatusCodes.Status403Forbidden,
        ApiErrorCodes.Forbidden,
        "Недостаточно системных прав для admin endpoint.");
}

static bool HasSystemPermission(ClaimsPrincipal principal, string claimType, string requiredPermission)
{
    var normalizedRequired = requiredPermission.Trim();
    if (string.IsNullOrWhiteSpace(normalizedRequired))
    {
        return false;
    }

    foreach (var claim in principal.FindAll(claimType))
    {
        if (string.Equals(claim.Value, normalizedRequired, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var split = claim.Value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (split.Any(value => string.Equals(value, normalizedRequired, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
    }

    return false;
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

static MarketplaceAuthPayload? ReadMarketplaceAuth(Dictionary<string, JsonElement> payload, string key)
{
    if (!payload.TryGetValue(key, out var value))
    {
        return null;
    }

    if (value.ValueKind != JsonValueKind.Object)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            $"Поле {key} должно быть объектом.");
    }

    var objectValue = value.EnumerateObject()
        .ToDictionary(x => x.Name, x => x.Value.Clone(), StringComparer.Ordinal);
    var scheme = ReadString(objectValue, "scheme");
    if (!MarketplaceAuthSchemes.All.Contains(scheme))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Поле marketplaceAuth.scheme содержит неподдерживаемое значение.");
    }

    if (!objectValue.TryGetValue("credentials", out var credentialsValue) || credentialsValue.ValueKind != JsonValueKind.Object)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Поле marketplaceAuth.credentials обязательно и должно быть объектом.");
    }

    var credentials = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var property in credentialsValue.EnumerateObject())
    {
        if (property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                $"Поле marketplaceAuth.credentials.{property.Name} должно быть непустой строкой.");
        }

        credentials[property.Name] = property.Value.GetString()!.Trim();
    }

    if (credentials.Count == 0)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Поле marketplaceAuth.credentials должно содержать минимум одно значение.");
    }

    if (string.Equals(scheme, MarketplaceAuthSchemes.GoldenKey, StringComparison.Ordinal)
        && !credentials.ContainsKey("golden_key"))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Для marketplaceAuth.scheme=golden_key требуется credentials.golden_key.");
    }
    if (string.Equals(scheme, MarketplaceAuthSchemes.Tokens, StringComparison.Ordinal))
    {
        if (!credentials.ContainsKey("token"))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "Для marketplaceAuth.scheme=tokens требуется credentials.token.");
        }

        if (!credentials.ContainsKey("ddg5"))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "Для marketplaceAuth.scheme=tokens требуется credentials.ddg5.");
        }
    }
    if (string.Equals(scheme, MarketplaceAuthSchemes.Cookies, StringComparison.Ordinal)
        && !credentials.ContainsKey("cookies"))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Для marketplaceAuth.scheme=cookies требуется credentials.cookies.");
    }

    return new MarketplaceAuthPayload(scheme, credentials);
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

static AccountsManagerMarketplaceAuth ToAccountsManagerMarketplaceAuth(MarketplaceAuthPayload payload)
{
    return new AccountsManagerMarketplaceAuth(payload.Scheme, payload.Credentials);
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

static AccountTypeDto ToAccountTypeDto(AccountsManagerAccountTypeDefinition definition)
{
    return new AccountTypeDto(
        definition.AccountTypeId,
        definition.Platform,
        definition.DisplayName,
        definition.Description,
        definition.WorkerProfileId,
        definition.Enabled,
        definition.SortOrder,
        definition.FormFields
            .Select(field => new AccountTypeFieldDto(
                field.Key,
                field.Label,
                field.InputType,
                field.Required,
                field.Secret,
                field.Placeholder,
                field.DefaultValue))
            .ToList());
}

static AdminAccountTypeDto ToAdminAccountTypeDto(AccountsManagerAccountTypeDefinition definition)
{
    return new AdminAccountTypeDto(
        definition.AccountTypeId,
        definition.Platform,
        definition.DisplayName,
        definition.Description,
        definition.WorkerProfileId,
        definition.Enabled,
        definition.SortOrder,
        definition.FormFields
            .Select(field => new AccountTypeFieldDto(
                field.Key,
                field.Label,
                field.InputType,
                field.Required,
                field.Secret,
                field.Placeholder,
                field.DefaultValue))
            .ToList(),
        new AdminAccountTypeRuntimeDto(
            definition.Runtime.AutospawnEnabled,
            definition.Runtime.WorkerImage,
            definition.Runtime.WorkerPathPrefix,
            definition.Runtime.HealthPath,
            definition.Runtime.ContainerPort,
            definition.Runtime.EnvironmentVariables,
            definition.Runtime.WorkerCommand));
}

static AdminWorkerServerDto ToAdminWorkerServerDto(AccountsManagerWorkerServerDefinition server)
{
    return new AdminWorkerServerDto(
        server.ServerId,
        server.BaseUrlTemplate,
        server.Status,
        server.Health,
        server.Capacity,
        server.CurrentLoad,
        server.DockerHost,
        server.DockerNetwork,
        server.LastHeartbeatAtUtc,
        new AdminWorkerServerRegistryDto(
            server.Registry.Enabled,
            server.Registry.Host,
            server.Registry.Username,
            server.Registry.HasToken,
            server.Registry.TokenUpdatedAtUtc),
        server.Metadata);
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

static string NormalizeIntegrationKey(string integrationKey)
{
    if (string.IsNullOrWhiteSpace(integrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "integrationKey обязателен.");
    }

    return integrationKey.Trim().ToLowerInvariant();
}

static bool IsSupportedIntegrationKey(string integrationKey)
{
    if (IntegrationKeys.All.Contains(integrationKey))
    {
        return true;
    }

    if (integrationKey.StartsWith("platform.", StringComparison.Ordinal))
    {
        var suffix = integrationKey["platform.".Length..];
        return suffix.Length > 0 && suffix.All(ch => char.IsLower(ch) || char.IsDigit(ch) || ch is '.' or '_' or '-');
    }

    return false;
}

static IReadOnlyCollection<string> NormalizeScopes(string integrationKey, IReadOnlyCollection<string>? requestedScopes)
{
    var requested = (requestedScopes ?? [])
        .Select(x => x?.Trim().ToLowerInvariant())
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    HashSet<string> allowed;
    List<string> defaults;

    if (IntegrationKeys.ServiceIntegrations.Contains(integrationKey))
    {
        allowed = new HashSet<string>(["read", "jobs"], StringComparer.OrdinalIgnoreCase);
        defaults = ["read", "jobs"];
    }
    else if (string.Equals(integrationKey, IntegrationKeys.Telegram, StringComparison.OrdinalIgnoreCase))
    {
        allowed = new HashSet<string>(["send"], StringComparer.OrdinalIgnoreCase);
        defaults = ["send"];
    }
    else if (integrationKey.StartsWith("platform.", StringComparison.Ordinal))
    {
        allowed = new HashSet<string>(["use"], StringComparer.OrdinalIgnoreCase);
        defaults = ["use"];
    }
    else
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Неподдерживаемый integration key.");
    }

    var scopes = requested.Count == 0 ? defaults : requested;
    if (scopes.Any(scope => !allowed.Contains(scope!)))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Переданы неподдерживаемые scopes.");
    }

    return scopes;
}

static string NormalizeScope(string scope)
{
    if (string.IsNullOrWhiteSpace(scope))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "scope обязателен.");
    }

    var normalized = scope.Trim().ToLowerInvariant();
    if (normalized is not ("read" or "jobs"))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "scope должен быть read или jobs.");
    }

    return normalized;
}

static IReadOnlyCollection<string> SplitScopes(string scopesCsv)
{
    return scopesCsv
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static NotificationOutboxEntity CreateNotificationOutbox(
    Guid projectId,
    string eventType,
    string message,
    string deduplicationKey)
{
    return new NotificationOutboxEntity
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId,
        Channel = IntegrationKeys.Telegram,
        EventType = eventType,
        DeduplicationKey = deduplicationKey,
        Status = "pending",
        AttemptCount = 0,
        NextAttemptAtUtc = DateTimeOffset.UtcNow,
        PayloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["eventType"] = eventType,
            ["message"] = message,
        }),
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };
}

static async Task EnsureActiveIntegrationGrantAsync(
    CoreDbContext dbContext,
    Guid projectId,
    string integrationKey,
    CancellationToken cancellationToken)
{
    var normalizedIntegrationKey = NormalizeIntegrationKey(integrationKey);
    var exists = await dbContext.ProjectIntegrationGrants.AnyAsync(
        x => x.ProjectId == projectId
             && x.IntegrationKey == normalizedIntegrationKey
             && x.Status == "active",
        cancellationToken);

    if (!exists)
    {
        throw new ApiErrorException(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "Интеграция не выдана проекту.");
    }
}

static string BuildPlatformIntegrationKey(string platform)
{
    return $"platform.{platform.Trim().ToLowerInvariant()}";
}

static string NormalizeProxyType(string? proxyType)
{
    if (string.IsNullOrWhiteSpace(proxyType))
    {
        return "http";
    }

    var value = proxyType.Trim().ToLowerInvariant();
    return value is "http" or "https" or "socks5" ? value : "http";
}

static string GenerateProjectServiceToken(string integrationKey)
{
    var random = Convert.ToBase64String(RandomNumberGenerator.GetBytes(36))
        .Replace("+", "-")
        .Replace("/", "_")
        .Replace("=", string.Empty);
    return $"ddcrm_{integrationKey}_{random}";
}

static string MaskToken(string token)
{
    if (token.Length <= 8)
    {
        return "****";
    }

    return $"{token[..4]}***{token[^4..]}";
}

static string GenerateTelegramLinkCode()
{
    var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(9))
        .Replace("+", string.Empty)
        .Replace("/", string.Empty)
        .Replace("=", string.Empty)
        .ToUpperInvariant();
    return value[..Math.Min(12, value.Length)];
}

static string NormalizeBindingType(string? bindingType)
{
    var normalized = (bindingType ?? "group").Trim().ToLowerInvariant();
    return normalized is "group" or "user"
        ? normalized
        : throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "bindingType должен быть group или user.");
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
    IReadOnlyCollection<AccountTypeFieldDto> FormFields);

public sealed record AccountTypeListResponse(string RequestId, IReadOnlyCollection<AccountTypeDto> Items);

public sealed record AdminAccountTypeRuntimeDto(
    bool AutospawnEnabled,
    string WorkerImage,
    string WorkerPathPrefix,
    string HealthPath,
    int ContainerPort,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    IReadOnlyCollection<string>? WorkerCommand);

public sealed record AdminAccountTypeDto(
    string AccountTypeId,
    string Platform,
    string DisplayName,
    string? Description,
    string WorkerProfileId,
    bool Enabled,
    int SortOrder,
    IReadOnlyCollection<AccountTypeFieldDto> FormFields,
    AdminAccountTypeRuntimeDto Runtime);

public sealed record AdminAccountTypeListResponse(
    string RequestId,
    IReadOnlyCollection<AdminAccountTypeDto> Items);

public sealed record AdminAccountTypeResponse(
    string RequestId,
    AdminAccountTypeDto AccountType);

public sealed record AdminAccountTypeUpsertRequest(
    string? Platform,
    string? DisplayName,
    string? Description,
    string? WorkerProfileId,
    bool? Enabled,
    int? SortOrder,
    IReadOnlyCollection<AccountTypeFieldDto>? FormFields,
    AdminAccountTypeRuntimeDto? Runtime);

public sealed record AdminWorkerServerDto(
    string ServerId,
    string BaseUrlTemplate,
    string Status,
    string Health,
    int Capacity,
    int CurrentLoad,
    string? DockerHost,
    string? DockerNetwork,
    DateTimeOffset? LastHeartbeatAtUtc,
    AdminWorkerServerRegistryDto Registry,
    IReadOnlyDictionary<string, object?> Metadata);

public sealed record AdminWorkerServerRegistryDto(
    bool Enabled,
    string Host,
    string? Username,
    bool HasToken,
    DateTimeOffset? TokenUpdatedAtUtc);

public sealed record AdminWorkerServerListResponse(
    string RequestId,
    IReadOnlyCollection<AdminWorkerServerDto> Items);

public sealed record AdminWorkerServerResponse(
    string RequestId,
    AdminWorkerServerDto WorkerServer);

public sealed record AdminWorkerServerUpsertRequest(
    string? BaseUrlTemplate,
    string? Status,
    int? Capacity,
    int? CurrentLoad,
    string? Health,
    string? DockerHost,
    string? DockerNetwork,
    AdminWorkerServerRegistryUpsertRequest? Registry,
    Dictionary<string, object?>? Metadata);

public sealed record AdminWorkerServerRegistryUpsertRequest(
    bool? Enabled,
    string? Host,
    string? Username,
    string? Token,
    bool? ClearToken);

public sealed record AdminIntegrationGrantUpsertRequest(IReadOnlyCollection<string>? Scopes);

public sealed record AdminIntegrationGrantDto(
    string IntegrationKey,
    string Status,
    IReadOnlyCollection<string> Scopes,
    DateTimeOffset GrantedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    string? CredentialStatus,
    string? CredentialMasked);

public sealed record AdminIntegrationGrantListResponse(
    string RequestId,
    IReadOnlyCollection<AdminIntegrationGrantDto> Items);

public sealed record AdminIntegrationGrantResponse(
    string RequestId,
    AdminIntegrationGrantDto Grant);

public sealed record AdminTelegramProxyProfileUpsertRequest(
    string Name,
    string? ProxyType,
    string Host,
    int Port,
    bool SetActive,
    bool ClearCredentials,
    string? Login,
    string? Password);

public sealed record AdminTelegramProxyProfileDto(
    Guid Id,
    string Name,
    string ProxyType,
    string Host,
    int Port,
    bool IsActive,
    bool HasCredentials,
    DateTimeOffset UpdatedAtUtc);

public sealed record AdminTelegramProxyProfileListResponse(
    string RequestId,
    IReadOnlyCollection<AdminTelegramProxyProfileDto> Items);

public sealed record AdminTelegramProxyProfileResponse(
    string RequestId,
    AdminTelegramProxyProfileDto Profile);

public sealed record ProjectIntegrationStatusDto(
    string IntegrationKey,
    string Status,
    IReadOnlyCollection<string> Scopes,
    string? CredentialStatus,
    string? CredentialMasked);

public sealed record TelegramBindingsSummaryDto(
    int GroupChats,
    int UserDmChats);

public sealed record ProjectIntegrationStatusResponse(
    string RequestId,
    IReadOnlyCollection<ProjectIntegrationStatusDto> Items,
    TelegramBindingsSummaryDto Telegram);

public sealed record TelegramLinkCodeCreateRequest(string? BindingType);

public sealed record TelegramLinkCodeResponse(
    string RequestId,
    string Code,
    string BindingType,
    DateTimeOffset ExpiresAtUtc);

public sealed record TelegramUserBindRequest(string ChatId, string? ChatTitle);

public sealed record TelegramLinkConfirmRequest(string Code, string ChatId, string? ChatTitle);

public sealed record CriticalNotificationRequest(string EventType, string Message);

public sealed record ProxyCredentialsMaskedDto(bool Configured, string? HostMasked, string? LoginMasked);

public sealed record ProxyCredentialsMaskedResponse(string RequestId, ProxyCredentialsMaskedDto ProxyCredentials);

public sealed record ProxyConfigDto(string Host, int Port, string Login, string Password);

public sealed record ProxyCredentialsRevealResponse(string RequestId, ProxyConfigDto ProxyConfig);

public sealed record ProxyResponse(string RequestId, JsonElement Result);

public sealed record GenericObjectResponse(string RequestId, IDictionary<string, object?> Data);

internal sealed record ProxyConfigPayload(string Host, int Port, string Login, string Password);

internal sealed record MarketplaceAuthPayload(string Scheme, IReadOnlyDictionary<string, string> Credentials);

internal static class MarketplaceAuthSchemes
{
    public const string GoldenKey = "golden_key";
    public const string Cookies = "cookies";
    public const string Tokens = "tokens";
    public const string LoginPassword = "login_password";

    public static readonly HashSet<string> All = new(
        [GoldenKey, Cookies, Tokens, LoginPassword],
        StringComparer.Ordinal);
}

public partial class Program;
