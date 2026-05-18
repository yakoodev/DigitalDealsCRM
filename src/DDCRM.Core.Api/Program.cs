using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DDCRM.Core.Api.AccountsManager;
using DDCRM.Core.Api.Billing;
using DDCRM.Core.Api.GatewayProxy;
using DDCRM.Core.Api.Integrations;
using DDCRM.Core.Api.Workflows;
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
builder.Services.Configure<TelegramNotificationOptions>(builder.Configuration.GetSection(TelegramNotificationOptions.SectionName));
builder.Services.PostConfigure<TelegramNotificationOptions>(options =>
{
    options.BotToken ??= builder.Configuration["TELEGRAM_NOTIFICATION_BOT_TOKEN"];
    options.LinkWebhookSecret ??= builder.Configuration["TELEGRAM_LINK_WEBHOOK_SECRET"];
});
builder.Services.Configure<IntegrationWorkerRuntimeOptions>(builder.Configuration.GetSection(IntegrationWorkerRuntimeOptions.SectionName));
builder.Services.Configure<WorkflowMessagePollingOptions>(builder.Configuration.GetSection(WorkflowMessagePollingOptions.SectionName));
builder.Services.PostConfigure<WorkflowMessagePollingOptions>(options =>
{
    options.InternalServiceToken ??= builder.Configuration["INTERNAL_API_SERVICE_AUTH_CLIENT_TOKEN"];
    options.WorkerServiceToken ??= builder.Configuration["WORKER_API_SERVICE_AUTH_CLIENT_TOKEN"];

    var routeRegistryBaseUrl = builder.Configuration["RouteRegistryClient:BaseUrl"]
                               ?? builder.Configuration["RouteRegistryClient__BaseUrl"];
    if (!string.IsNullOrWhiteSpace(routeRegistryBaseUrl))
    {
        options.RouteRegistryBaseUrl = routeRegistryBaseUrl;
    }
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
builder.Services.AddHttpClient<IEntitlementCheckClient, EntitlementCheckHttpClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<EntitlementCheckClientOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});
builder.Services.AddHttpClient<WorkflowWorkerBridgeClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient("integration-ui-proxy", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.Configure<IntegrationEmbeddedUiOptions>(builder.Configuration.GetSection("IntegrationEmbeddedUi"));
builder.Services.AddScoped<IProjectServiceIntegrationClient>(serviceProvider => serviceProvider.GetRequiredService<FunPayStatIntegrationClient>());
builder.Services.AddScoped<ProjectServiceIntegrationRegistry>();
builder.Services.AddSingleton<ProjectSecretCrypto>();
builder.Services.AddScoped<ICustomHttpIntegrationInvoker, CustomHttpIntegrationInvoker>();
builder.Services.AddScoped<WorkflowNodeExecutorRegistry>();
builder.Services.AddScoped<IWorkflowNodeExecutor, PurchaseStartNodeExecutor>();
builder.Services.AddScoped<IWorkflowNodeExecutor, MessageStartNodeExecutor>();
builder.Services.AddScoped<IWorkflowNodeExecutor, ReviewStartNodeExecutor>();
builder.Services.AddScoped<IWorkflowNodeExecutor, ConditionNodeExecutor>();
builder.Services.AddScoped<IWorkflowNodeExecutor, SetVariablesNodeExecutor>();
builder.Services.AddScoped<IWorkflowNodeExecutor, LoadOfferNodeExecutor>();
builder.Services.AddScoped<IWorkflowNodeExecutor, SelectAccountPriorityFallbackNodeExecutor>();
builder.Services.AddScoped<IWorkflowNodeExecutor, InvokeWorkerActionNodeExecutor>();
builder.Services.AddScoped<IWorkflowNodeExecutor, InvokeCustomHttpNodeExecutor>();
builder.Services.AddScoped<IWorkflowNodeExecutor, SteamActionNodeExecutor>();
builder.Services.AddScoped<IWorkflowNodeExecutor, TaskNodeExecutor>();
builder.Services.AddScoped<IWorkflowNodeExecutor, SendBuyerResponseNodeExecutor>();
builder.Services.AddScoped<IWorkflowNodeExecutor, NotifyNodeExecutor>();
builder.Services.AddScoped<IWorkflowNodeExecutor, EndNodeExecutor>();
builder.Services.AddScoped<WorkflowExecutionEngine>();
builder.Services.AddScoped<TelegramNotificationSender>();
builder.Services.AddHostedService<ServiceCredentialSyncBackgroundService>();
builder.Services.AddHostedService<IntegrationWorkerRuntimeBackgroundService>();
builder.Services.AddHostedService<NotificationOutboxBackgroundService>();
builder.Services.AddHostedService<TelegramBotPollingBackgroundService>();
builder.Services.AddHostedService<WorkflowExecutionBackgroundService>();
builder.Services.AddHostedService<WorkflowMessagePollingBackgroundService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtIssuer = builder.Configuration["EXTERNAL_API_JWT_ISSUER"] ?? "ddcrm-local";
        var jwtAudience = builder.Configuration["EXTERNAL_API_JWT_AUDIENCE"] ?? "ddcrm-api";
        var jwtSigningKey = builder.Configuration["EXTERNAL_API_JWT_SIGNING_KEY"]
                            ?? "replace-this-signing-key-with-at-least-32-characters";

        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
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

var app = builder.Build();

var jwtIssuer = app.Configuration["EXTERNAL_API_JWT_ISSUER"] ?? "ddcrm-local";
var jwtAudience = app.Configuration["EXTERNAL_API_JWT_AUDIENCE"] ?? "ddcrm-api";
var jwtSigningKey = app.Configuration["EXTERNAL_API_JWT_SIGNING_KEY"]
                    ?? "replace-this-signing-key-with-at-least-32-characters";
var jwtSigningCredentials = new SigningCredentials(
    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
    SecurityAlgorithms.HmacSha256);
var authTokenLifetimeMinutes = Math.Clamp(
    app.Configuration.GetValue("EXTERNAL_API_AUTH_TOKEN_LIFETIME_MINUTES", 60),
    5,
    1440);
var systemPermissionClaimType =
    app.Configuration["EXTERNAL_API_SYSTEM_PERMISSION_CLAIM_TYPE"]
    ?? "ddcrm.system.permissions";
var systemPermissionClaimValue =
    app.Configuration["EXTERNAL_API_SYSTEM_PERMISSION_CLAIM_VALUE"]
    ?? "system.accountManager.manage";
var systemIntegrationsPermissionValue =
    app.Configuration["EXTERNAL_API_SYSTEM_INTEGRATIONS_PERMISSION_CLAIM_VALUE"]
    ?? "system.integrations.manage";
var superAdminEmail = app.Configuration["EXTERNAL_API_SUPER_ADMIN_EMAIL"];
var superAdminPassword = app.Configuration["EXTERNAL_API_SUPER_ADMIN_PASSWORD"];
var superAdminDisplayName = app.Configuration["EXTERNAL_API_SUPER_ADMIN_DISPLAY_NAME"] ?? "Super Admin";
var authProviderTelegramEnabled = app.Configuration.GetValue("EXTERNAL_API_AUTH_PROVIDER_TELEGRAM_ENABLED", false);
var authProviderGithubEnabled = app.Configuration.GetValue("EXTERNAL_API_AUTH_PROVIDER_GITHUB_ENABLED", false);
var authProviderGoogleEnabled = app.Configuration.GetValue("EXTERNAL_API_AUTH_PROVIDER_GOOGLE_ENABLED", false);
var offersFeatureEnabled = app.Configuration.GetValue("FEATURE_OFFERS_ENABLED", true);
var workflowsFeatureEnabled = app.Configuration.GetValue("FEATURE_WORKFLOWS_ENABLED", true);
var customHttpFeatureEnabled = app.Configuration.GetValue("FEATURE_CUSTOM_HTTP_INTEGRATIONS_ENABLED", true);

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

    await EnsureSuperAdminAccountAsync(
        db,
        superAdminEmail,
        superAdminPassword,
        superAdminDisplayName,
        [systemPermissionClaimValue, systemIntegrationsPermissionValue]);
}

app.UseDdcrmCommonPipeline();
app.UseCors("external-cors");
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (httpContext, next) =>
{
    if (httpContext.User.Identity?.IsAuthenticated != true)
    {
        await next();
        return;
    }

    var path = httpContext.Request.Path;
    var isAuthPath = path.StartsWithSegments("/v1/auth/change-password", StringComparison.OrdinalIgnoreCase)
                     || path.StartsWithSegments("/v1/auth/me", StringComparison.OrdinalIgnoreCase);
    if (isAuthPath || HttpMethods.IsOptions(httpContext.Request.Method))
    {
        await next();
        return;
    }

    var userIdValue = httpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(userIdValue, out var userId))
    {
        await next();
        return;
    }

    var dbContext = httpContext.RequestServices.GetRequiredService<CoreDbContext>();
    var requiresPasswordChange = await dbContext.AuthUsers
        .Where(x => x.Id == userId)
        .Select(x => x.ForcePasswordChange)
        .SingleOrDefaultAsync(httpContext.RequestAborted);

    if (!requiresPasswordChange)
    {
        await next();
        return;
    }

    httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
    httpContext.Response.ContentType = "application/json";

    var requestId = httpContext.GetOrCreateRequestId();
    var payload = new ErrorResponse(
        ApiErrorCodes.Forbidden,
        "Требуется смена пароля перед продолжением работы.",
        requestId,
        new Dictionary<string, object?>
        {
            ["passwordChangeRequired"] = true,
        });
    await httpContext.Response.WriteAsJsonAsync(payload);
});

app.MapGet("/health", (HttpContext httpContext) =>
    Results.Ok(new GenericObjectResponse(
        httpContext.GetOrCreateRequestId(),
        new Dictionary<string, object?>
        {
            ["status"] = "ok",
        })));

app.MapMethods("/v1/{*path}", ["OPTIONS"], () => Results.Ok());

app.MapPost("/v1/auth/register", async (
    HttpContext httpContext,
    AuthRegisterRequest request,
    CoreDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var email = NormalizeEmail(request.Email);
    ValidatePassword(request.Password, "password");

    var exists = await dbContext.AuthUsers.AnyAsync(x => x.EmailNormalized == email, cancellationToken);
    if (exists)
    {
        throw new ApiErrorException(
            StatusCodes.Status409Conflict,
            ApiErrorCodes.Conflict,
            "Пользователь с таким email уже зарегистрирован.");
    }

    var now = DateTimeOffset.UtcNow;
    var displayName = NormalizeDisplayName(request.DisplayName, email);
    var user = new AuthUserEntity
    {
        Id = Guid.NewGuid(),
        Email = email,
        EmailNormalized = email,
        DisplayName = displayName,
        PasswordHash = HashPassword(request.Password),
        SystemPermissionsCsv = null,
        ForcePasswordChange = false,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
    };

    dbContext.AuthUsers.Add(user);
    await dbContext.SaveChangesAsync(cancellationToken);

    var session = BuildAuthSessionResponse(
        httpContext,
        user,
        jwtIssuer,
        jwtAudience,
        jwtSigningCredentials,
        authTokenLifetimeMinutes,
        systemPermissionClaimType);

    return Results.Created($"/v1/auth/users/{user.Id}", session);
});

app.MapPost("/v1/auth/login", async (
    HttpContext httpContext,
    AuthLoginRequest request,
    CoreDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var email = NormalizeEmail(request.Email);
    var user = await dbContext.AuthUsers.SingleOrDefaultAsync(
        x => x.EmailNormalized == email,
        cancellationToken);

    if (user is null || !VerifyPassword(request.Password, user.PasswordHash))
    {
        throw new ApiErrorException(
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.Unauthorized,
            "Неверный email или пароль.");
    }

    user.LastLoginAtUtc = DateTimeOffset.UtcNow;
    await dbContext.SaveChangesAsync(cancellationToken);

    var session = BuildAuthSessionResponse(
        httpContext,
        user,
        jwtIssuer,
        jwtAudience,
        jwtSigningCredentials,
        authTokenLifetimeMinutes,
        systemPermissionClaimType);

    return Results.Ok(session);
});

app.MapGet("/v1/auth/providers", (HttpContext httpContext) =>
{
    var items = new List<AuthProviderDto>
    {
        new("local", "Email + Password", true, "active"),
        new("telegram", "Telegram", authProviderTelegramEnabled, authProviderTelegramEnabled ? "planned" : "disabled"),
        new("github", "GitHub", authProviderGithubEnabled, authProviderGithubEnabled ? "planned" : "disabled"),
        new("google", "Google", authProviderGoogleEnabled, authProviderGoogleEnabled ? "planned" : "disabled"),
    };

    return Results.Ok(new AuthProviderListResponse(httpContext.GetOrCreateRequestId(), items));
});

app.MapPost("/v1/auth/change-password", async (
    HttpContext httpContext,
    AuthChangePasswordRequest request,
    CoreDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    ValidatePassword(request.NewPassword, "newPassword");
    if (string.Equals(request.CurrentPassword, request.NewPassword, StringComparison.Ordinal))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Новый пароль должен отличаться от текущего.");
    }

    var userId = GetCurrentUserId(httpContext);
    var user = await dbContext.AuthUsers.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
    if (user is null)
    {
        throw new ApiErrorException(
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.Unauthorized,
            "Пользователь сессии не найден.");
    }

    if (!VerifyPassword(request.CurrentPassword, user.PasswordHash))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Текущий пароль введен неверно.");
    }

    user.PasswordHash = HashPassword(request.NewPassword);
    user.ForcePasswordChange = false;
    user.UpdatedAtUtc = DateTimeOffset.UtcNow;

    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new AckResponse(httpContext.GetOrCreateRequestId(), "password_changed"));
}).RequireAuthorization();

app.MapGet("/v1/auth/me", async (
    HttpContext httpContext,
    CoreDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var userId = GetCurrentUserId(httpContext);
    var user = await dbContext.AuthUsers.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
    if (user is null)
    {
        throw new ApiErrorException(
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.Unauthorized,
            "Пользователь сессии не найден.");
    }

    var session = BuildAuthSessionResponse(
        httpContext,
        user,
        jwtIssuer,
        jwtAudience,
        jwtSigningCredentials,
        authTokenLifetimeMinutes,
        systemPermissionClaimType);

    return Results.Ok(session);
}).RequireAuthorization();

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
    var runtimes = await dbContext.ProjectIntegrationWorkerRuntimes
        .AsNoTracking()
        .Where(x => x.ProjectId == projectId)
        .ToListAsync(cancellationToken);
    var runtimeByIntegration = runtimes
        .GroupBy(x => x.IntegrationKey, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            x => x.Key,
            x => x.OrderByDescending(item => item.IsDefault).ThenBy(item => item.CreatedAtUtc).First(),
            StringComparer.OrdinalIgnoreCase);

    var items = grants.Select(grant =>
    {
        credentials.TryGetValue(grant.IntegrationKey, out var credential);
        runtimeByIntegration.TryGetValue(grant.IntegrationKey, out var runtime);
        return new AdminIntegrationGrantDto(
            grant.IntegrationKey,
            IntegrationKeys.ResolveIntegrationType(grant.IntegrationKey),
            grant.Status,
            SplitScopes(grant.ScopesCsv),
            Math.Max(1, grant.MaxInstances),
            grant.GrantedAtUtc,
            grant.RevokedAtUtc,
            credential?.Status,
            credential?.SecretMasked,
            runtime?.Status,
            runtime?.RuntimeAccountId,
            runtime?.LastError);
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
    var maxInstances = NormalizeIntegrationMaxInstances(normalizedIntegrationKey, request.MaxInstances);

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
            ProjectIntegrationWorkerRuntimeEntity? runtime = null;
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
            else if (IntegrationKeys.WorkerIntegrations.Contains(normalizedIntegrationKey))
            {
                // Legacy service credential rows for Steam are fail-closed after transport switch to worker.
                var legacyCredential = await dbContext.ProjectServiceCredentials
                    .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IntegrationKey == normalizedIntegrationKey, ct);
                if (legacyCredential is not null)
                {
                    legacyCredential.Status = "revoked";
                    legacyCredential.RevokedAtUtc = now;
                    legacyCredential.UpdatedAtUtc = now;
                }

                runtime = await LoadDefaultWorkerRuntimeAsync(dbContext, projectId, normalizedIntegrationKey, ct, tracking: true);
                runtime = EnsureWorkerRuntime(
                    dbContext,
                    runtime,
                    projectId,
                    normalizedIntegrationKey,
                    now,
                    status: "pending_provision",
                    clearDeprovisionedAt: true);
                await QueueWorkerRuntimeOutboxOperationAsync(
                    dbContext,
                    projectId,
                    normalizedIntegrationKey,
                    runtime.RuntimeAccountId,
                    operation: "provision",
                    now,
                    suppressProvisionOperations: false,
                    suppressDeprovisionOperations: true,
                    ct);
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
                        IntegrationKeys.ResolveIntegrationType(normalizedIntegrationKey),
                        grant.Status,
                        scopes,
                        Math.Max(1, grant.MaxInstances),
                        grant.GrantedAtUtc,
                        grant.RevokedAtUtc,
                        credential?.Status,
                        credential?.SecretMasked,
                        runtime?.Status,
                        runtime?.RuntimeAccountId,
                        runtime?.LastError)));
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

            if (IntegrationKeys.ServiceIntegrations.Contains(normalizedIntegrationKey))
            {
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
            }
            else if (IntegrationKeys.WorkerIntegrations.Contains(normalizedIntegrationKey))
            {
                await SupersedeWorkerRuntimeOutboxOperationsAsync(
                    dbContext,
                    projectId,
                    normalizedIntegrationKey,
                    runtimeAccountId: null,
                    operation: "provision",
                    now,
                    note: "Suppressed by integration revoke.",
                    ct);

                var runtime = await LoadDefaultWorkerRuntimeAsync(dbContext, projectId, normalizedIntegrationKey, ct, tracking: true);

                if (runtime is not null)
                {
                    runtime.Status = "revoking";
                    runtime.LastError = null;
                    runtime.UpdatedAtUtc = now;
                    await QueueWorkerRuntimeOutboxOperationAsync(
                        dbContext,
                        projectId,
                        normalizedIntegrationKey,
                        runtime.RuntimeAccountId,
                        operation: "deprovision",
                        now,
                        suppressProvisionOperations: true,
                        suppressDeprovisionOperations: false,
                        ct);
                }

                var legacyCredential = await dbContext.ProjectServiceCredentials
                    .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IntegrationKey == normalizedIntegrationKey, ct);
                if (legacyCredential is not null)
                {
                    legacyCredential.Status = "revoked";
                    legacyCredential.RevokedAtUtc = now;
                    legacyCredential.UpdatedAtUtc = now;
                }
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

external.MapPost("/admin/integrations/projects/{projectId:guid}/grants/{integrationKey}/runtime/provision", async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    EnsureSystemPermission(httpContext, systemPermissionClaimType, systemIntegrationsPermissionValue);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    var normalizedIntegrationKey = NormalizeIntegrationKey(integrationKey);

    if (!IntegrationKeys.WorkerIntegrations.Contains(normalizedIntegrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Runtime provision поддержан только для worker-интеграций.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:integrationRuntimeProvision:{projectId}:{normalizedIntegrationKey}",
        idempotencyKey,
        async ct =>
        {
            var grant = await dbContext.ProjectIntegrationGrants
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IntegrationKey == normalizedIntegrationKey, ct);
            if (grant is null || grant.Status != "active")
            {
                throw new ApiErrorException(StatusCodes.Status409Conflict, ApiErrorCodes.Conflict, "Для provision требуется активный grant.");
            }

            var now = DateTimeOffset.UtcNow;
            var runtime = await LoadDefaultWorkerRuntimeAsync(dbContext, projectId, normalizedIntegrationKey, ct, tracking: true);
            runtime = EnsureWorkerRuntime(
                dbContext,
                runtime,
                projectId,
                normalizedIntegrationKey,
                now,
                status: "pending_provision",
                clearDeprovisionedAt: true);
            await QueueWorkerRuntimeOutboxOperationAsync(
                dbContext,
                projectId,
                normalizedIntegrationKey,
                runtime.RuntimeAccountId,
                operation: "provision",
                now,
                suppressProvisionOperations: false,
                suppressDeprovisionOperations: true,
                ct);

            await dbContext.SaveChangesAsync(ct);
            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "queued"));
        },
        cancellationToken);
});

external.MapPost("/admin/integrations/projects/{projectId:guid}/grants/{integrationKey}/runtime/deprovision", async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    EnsureSystemPermission(httpContext, systemPermissionClaimType, systemIntegrationsPermissionValue);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    var normalizedIntegrationKey = NormalizeIntegrationKey(integrationKey);

    if (!IntegrationKeys.WorkerIntegrations.Contains(normalizedIntegrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Runtime deprovision поддержан только для worker-интеграций.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:integrationRuntimeDeprovision:{projectId}:{normalizedIntegrationKey}",
        idempotencyKey,
        async ct =>
        {
            var runtime = await LoadDefaultWorkerRuntimeAsync(dbContext, projectId, normalizedIntegrationKey, ct, tracking: true);

            if (runtime is null)
            {
                return new IdempotentExecutionResult(
                    StatusCodes.Status200OK,
                    new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
            }

            var now = DateTimeOffset.UtcNow;
            await SupersedeWorkerRuntimeOutboxOperationsAsync(
                dbContext,
                projectId,
                normalizedIntegrationKey,
                runtime.RuntimeAccountId,
                operation: "provision",
                now,
                note: "Suppressed by manual deprovision.",
                ct);
            runtime.Status = "revoking";
            runtime.LastError = null;
            runtime.UpdatedAtUtc = now;
            await QueueWorkerRuntimeOutboxOperationAsync(
                dbContext,
                projectId,
                normalizedIntegrationKey,
                runtime.RuntimeAccountId,
                operation: "deprovision",
                now,
                suppressProvisionOperations: true,
                suppressDeprovisionOperations: false,
                ct);

            await dbContext.SaveChangesAsync(ct);
            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "queued"));
        },
        cancellationToken);
});

external.MapPost("/admin/integrations/projects/{projectId:guid}/grants/{integrationKey}/runtime/restart", async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    EnsureSystemPermission(httpContext, systemPermissionClaimType, systemIntegrationsPermissionValue);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    var normalizedIntegrationKey = NormalizeIntegrationKey(integrationKey);

    if (!IntegrationKeys.WorkerIntegrations.Contains(normalizedIntegrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Runtime restart поддержан только для worker-интеграций.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:integrationRuntimeRestart:{projectId}:{normalizedIntegrationKey}",
        idempotencyKey,
        async ct =>
        {
            var grant = await dbContext.ProjectIntegrationGrants
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IntegrationKey == normalizedIntegrationKey, ct);
            if (grant is null || grant.Status != "active")
            {
                throw new ApiErrorException(StatusCodes.Status409Conflict, ApiErrorCodes.Conflict, "Для restart требуется активный grant.");
            }

            var now = DateTimeOffset.UtcNow;
            var runtime = await LoadDefaultWorkerRuntimeAsync(dbContext, projectId, normalizedIntegrationKey, ct, tracking: true);
            var hadActiveRuntime = runtime?.ProvisionedAtUtc is not null || string.Equals(runtime?.Status, "active", StringComparison.OrdinalIgnoreCase);
            runtime = EnsureWorkerRuntime(
                dbContext,
                runtime,
                projectId,
                normalizedIntegrationKey,
                now,
                status: "pending_provision",
                clearDeprovisionedAt: true);

            if (hadActiveRuntime)
            {
                await QueueWorkerRuntimeOutboxOperationAsync(
                    dbContext,
                    projectId,
                    normalizedIntegrationKey,
                    runtime.RuntimeAccountId,
                    operation: "deprovision",
                    now,
                    suppressProvisionOperations: true,
                    suppressDeprovisionOperations: false,
                    ct);
            }

            await QueueWorkerRuntimeOutboxOperationAsync(
                dbContext,
                projectId,
                normalizedIntegrationKey,
                runtime.RuntimeAccountId,
                operation: "provision",
                now.AddSeconds(1),
                suppressProvisionOperations: false,
                suppressDeprovisionOperations: false,
                ct);

            await dbContext.SaveChangesAsync(ct);
            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "queued"));
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

    var normalizedName = request.Name.Trim();

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
            var duplicateNameExists = await dbContext.TelegramProxyProfiles
                .AnyAsync(x => x.Name == normalizedName && x.Id != proxyId, ct);
            if (duplicateNameExists)
            {
                throw new ApiErrorException(StatusCodes.Status409Conflict, ApiErrorCodes.Conflict, "Профиль с таким name уже существует.");
            }

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
                    Name = normalizedName,
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
                entity.Name = normalizedName;
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

                entity.IsActive = request.SetActive;
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

external.MapPost("/admin/integrations/telegram/test-message", async (
    HttpContext httpContext,
    AdminTelegramTestMessageRequest request,
    CoreDbContext dbContext,
    ProjectSecretCrypto crypto,
    TelegramNotificationSender sender,
    CancellationToken cancellationToken) =>
{
    EnsureSystemPermission(httpContext, systemPermissionClaimType, systemIntegrationsPermissionValue);

    if (string.IsNullOrWhiteSpace(request.ChatId))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "chatId обязателен.");
    }

    var chatId = request.ChatId.Trim();
    var message = string.IsNullOrWhiteSpace(request.Message)
        ? $"DDCRM Telegram test {DateTimeOffset.UtcNow:O}"
        : request.Message.Trim();

    var proxy = await ResolveActiveTelegramProxyAsync(dbContext, crypto, cancellationToken);
    var sendResult = await sender.SendMessageWithDiagnosticsAsync(chatId, message, proxy, cancellationToken);
    if (sendResult.Status != "ok")
    {
        throw new ApiErrorException(
            StatusCodes.Status502BadGateway,
            ApiErrorCodes.InternalError,
            $"Не удалось отправить Telegram test message: {sendResult.ReasonCode}: {sendResult.ErrorMessage}");
    }

    return Results.Ok(new AdminTelegramTestMessageResponse(
        httpContext.GetOrCreateRequestId(),
        "completed",
        sendResult.EffectivePath ?? "unknown",
        sendResult.ReasonCode));
});

external.MapPost("/admin/integrations/telegram/test-connectivity", async (
    HttpContext httpContext,
    CoreDbContext dbContext,
    ProjectSecretCrypto crypto,
    TelegramNotificationSender sender,
    CancellationToken cancellationToken) =>
{
    EnsureSystemPermission(httpContext, systemPermissionClaimType, systemIntegrationsPermissionValue);

    var proxy = await ResolveActiveTelegramProxyAsync(dbContext, crypto, cancellationToken);
    try
    {
        var result = await sender.GetMeWithDiagnosticsAsync(proxy, cancellationToken);
        return Results.Ok(new AdminTelegramConnectivityResponse(
            httpContext.GetOrCreateRequestId(),
            result.Status,
            result.EffectivePath,
            result.ProxyAttempted,
            result.ProxySucceeded,
            result.DirectAttempted,
            result.DirectSucceeded,
            result.ReasonCode,
            result.ProxyError,
            result.DirectError,
            result.Identity?.BotId,
            result.Identity?.Username,
            result.Identity?.FirstName));
    }
    catch (Exception exception)
    {
        var connectivityFailure = ParseTelegramConnectivityFailure(exception.Message);
        return Results.Ok(new AdminTelegramConnectivityResponse(
            httpContext.GetOrCreateRequestId(),
            "error",
            connectivityFailure.EffectivePath,
            connectivityFailure.ProxyAttempted,
            connectivityFailure.ProxySucceeded,
            connectivityFailure.DirectAttempted,
            connectivityFailure.DirectSucceeded,
            connectivityFailure.ReasonCode,
            connectivityFailure.ProxyError,
            connectivityFailure.DirectError,
            null,
            null,
            null));
    }
});

external.MapGet("/admin/integrations/custom-http/allowlist", async (
    HttpContext httpContext,
    CoreDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    EnsureSystemPermission(httpContext, systemPermissionClaimType, systemIntegrationsPermissionValue);

    var items = await dbContext.AdminCustomHttpAllowlist
        .AsNoTracking()
        .OrderBy(x => x.HostPattern)
        .Select(x => new AdminCustomHttpAllowlistEntryDto(
            x.Id,
            x.HostPattern,
            x.IsActive,
            x.Note,
            x.UpdatedAtUtc))
        .ToListAsync(cancellationToken);

    return Results.Ok(new AdminCustomHttpAllowlistListResponse(
        httpContext.GetOrCreateRequestId(),
        items));
});

external.MapPut("/admin/integrations/custom-http/allowlist/{entryId:guid}", async (
    HttpContext httpContext,
    Guid entryId,
    AdminCustomHttpAllowlistUpsertRequest request,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    EnsureSystemPermission(httpContext, systemPermissionClaimType, systemIntegrationsPermissionValue);
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:adminCustomHttpAllowlistUpsert:{entryId}",
        idempotencyKey,
        async ct =>
        {
            var now = DateTimeOffset.UtcNow;
            var normalizedPattern = NormalizeAllowlistHostPattern(request.HostPattern);
            var entity = await dbContext.AdminCustomHttpAllowlist
                .SingleOrDefaultAsync(x => x.Id == entryId, ct);
            if (entity is null)
            {
                entity = new AdminCustomHttpAllowlistEntity
                {
                    Id = entryId,
                    HostPattern = normalizedPattern,
                    IsActive = request.IsActive,
                    Note = request.Note?.Trim(),
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    UpdatedByUserId = actorId,
                };
                dbContext.AdminCustomHttpAllowlist.Add(entity);
            }
            else
            {
                entity.HostPattern = normalizedPattern;
                entity.IsActive = request.IsActive;
                entity.Note = request.Note?.Trim();
                entity.UpdatedAtUtc = now;
                entity.UpdatedByUserId = actorId;
            }

            await dbContext.SaveChangesAsync(ct);
            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AdminCustomHttpAllowlistResponse(
                    httpContext.GetOrCreateRequestId(),
                    new AdminCustomHttpAllowlistEntryDto(
                        entity.Id,
                        entity.HostPattern,
                        entity.IsActive,
                        entity.Note,
                        entity.UpdatedAtUtc)));
        },
        cancellationToken);
});

external.MapDelete("/admin/integrations/custom-http/allowlist/{entryId:guid}", async (
    HttpContext httpContext,
    Guid entryId,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    EnsureSystemPermission(httpContext, systemPermissionClaimType, systemIntegrationsPermissionValue);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:adminCustomHttpAllowlistDelete:{entryId}",
        idempotencyKey,
        async ct =>
        {
            var entity = await dbContext.AdminCustomHttpAllowlist
                .SingleOrDefaultAsync(x => x.Id == entryId, ct);
            if (entity is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Allowlist entry не найден.");
            }

            dbContext.AdminCustomHttpAllowlist.Remove(entity);
            await dbContext.SaveChangesAsync(ct);
            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
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
    var runtimes = await dbContext.ProjectIntegrationWorkerRuntimes
        .AsNoTracking()
        .Where(x => x.ProjectId == projectId)
        .ToListAsync(cancellationToken);
    var runtimeByIntegration = runtimes
        .GroupBy(x => x.IntegrationKey, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            x => x.Key,
            x => x.OrderByDescending(item => item.IsDefault).ThenBy(item => item.CreatedAtUtc).First(),
            StringComparer.OrdinalIgnoreCase);

    var groupChats = await dbContext.TelegramChatBindings
        .AsNoTracking()
        .CountAsync(x => x.ProjectId == projectId && x.BindingType == "group", cancellationToken);
    var userChats = await dbContext.TelegramChatBindings
        .AsNoTracking()
        .CountAsync(x => x.ProjectId == projectId && x.BindingType == "user", cancellationToken);

    var items = grants.Select(grant =>
    {
        credentials.TryGetValue(grant.IntegrationKey, out var credential);
        runtimeByIntegration.TryGetValue(grant.IntegrationKey, out var runtime);
        return new ProjectIntegrationStatusDto(
            grant.IntegrationKey,
            IntegrationKeys.ResolveIntegrationType(grant.IntegrationKey),
            grant.Status,
            SplitScopes(grant.ScopesCsv),
            Math.Max(1, grant.MaxInstances),
            credential?.Status,
            credential?.SecretMasked,
            runtime?.Status,
            runtime?.RuntimeAccountId,
            runtime?.LastError);
    }).ToList();

    return Results.Ok(new ProjectIntegrationStatusResponse(
        httpContext.GetOrCreateRequestId(),
        items,
        new TelegramBindingsSummaryDto(groupChats, userChats)));
});

external.MapGet("/projects/{projectId:guid}/integrations/{integrationKey}/instances", async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    CoreDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);

    var normalizedIntegrationKey = NormalizeIntegrationKey(integrationKey);
    if (!IntegrationKeys.WorkerIntegrations.Contains(normalizedIntegrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Instances поддержаны только для worker-интеграций.");
    }

    var grant = await dbContext.ProjectIntegrationGrants
        .AsNoTracking()
        .SingleOrDefaultAsync(
            x => x.ProjectId == projectId
                 && x.IntegrationKey == normalizedIntegrationKey
                 && x.Status == "active",
            cancellationToken);
    if (grant is null)
    {
        throw new ApiErrorException(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "Интеграция не выдана проекту.");
    }

    var items = await dbContext.ProjectIntegrationWorkerRuntimes
        .AsNoTracking()
        .Where(x => x.ProjectId == projectId && x.IntegrationKey == normalizedIntegrationKey)
        .OrderByDescending(x => x.IsDefault)
        .ThenBy(x => x.CreatedAtUtc)
        .ToListAsync(cancellationToken);

    return Results.Ok(new ProjectIntegrationInstanceListResponse(
        httpContext.GetOrCreateRequestId(),
        normalizedIntegrationKey,
        Math.Max(1, grant.MaxInstances),
        items.Select(ToProjectIntegrationInstanceDto).ToList()));
});

external.MapPost("/projects/{projectId:guid}/integrations/{integrationKey}/instances", async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    Dictionary<string, JsonElement>? request,
    CoreDbContext dbContext,
    ProjectSecretCrypto crypto,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);

    var normalizedIntegrationKey = NormalizeIntegrationKey(integrationKey);
    if (!IntegrationKeys.WorkerIntegrations.Contains(normalizedIntegrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Instances поддержаны только для worker-интеграций.");
    }

    var payload = request ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    var requestedDisplayName = TryReadString(payload, "displayName");
    var autoProvision = payload.TryGetValue("autoProvision", out var autoProvisionValue)
        ? ReadBoolValue(autoProvisionValue, "autoProvision")
        : true;
    var makeDefault = payload.TryGetValue("makeDefault", out var makeDefaultValue)
        ? ReadBoolValue(makeDefaultValue, "makeDefault")
        : false;
    var proxyConfig = ReadProxyConfig(payload, "proxyConfig", required: false);
    var mailConfigRequested = payload.ContainsKey("mailConfig");
    var mailConfig = mailConfigRequested ? ReadMailConfig(payload, "mailConfig") : null;

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:integrationInstancesCreate:{projectId}:{normalizedIntegrationKey}",
        idempotencyKey,
        async ct =>
        {
            var grant = await dbContext.ProjectIntegrationGrants
                .SingleOrDefaultAsync(
                    x => x.ProjectId == projectId
                         && x.IntegrationKey == normalizedIntegrationKey
                         && x.Status == "active",
                    ct);
            if (grant is null)
            {
                throw new ApiErrorException(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "Интеграция не выдана проекту.");
            }

            var existingInstances = await dbContext.ProjectIntegrationWorkerRuntimes
                .Where(x => x.ProjectId == projectId && x.IntegrationKey == normalizedIntegrationKey)
                .OrderBy(x => x.CreatedAtUtc)
                .ToListAsync(ct);
            var maxInstances = Math.Max(1, grant.MaxInstances);
            if (existingInstances.Count >= maxInstances)
            {
                throw new ApiErrorException(
                    StatusCodes.Status409Conflict,
                    ApiErrorCodes.Conflict,
                    $"Достигнут лимит integration instances ({maxInstances}).");
            }

            var now = DateTimeOffset.UtcNow;
            var displayName = string.IsNullOrWhiteSpace(requestedDisplayName)
                ? $"{normalizedIntegrationKey}-instance-{existingInstances.Count + 1}"
                : requestedDisplayName;

            if ((mailConfigRequested || mailConfig is not null) && proxyConfig is null)
            {
                throw new ApiErrorException(
                    StatusCodes.Status400BadRequest,
                    ApiErrorCodes.ValidationError,
                    "При указании mailConfig обязателен proxyConfig.");
            }

            if (makeDefault || existingInstances.Count == 0)
            {
                foreach (var item in existingInstances)
                {
                    item.IsDefault = false;
                }
            }

            var runtime = new ProjectIntegrationWorkerRuntimeEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                IntegrationKey = normalizedIntegrationKey,
                InstanceDisplayName = displayName,
                IsDefault = makeDefault || existingInstances.Count == 0,
                RuntimeAccountId = Guid.NewGuid(),
                Status = autoProvision ? "pending_provision" : "draft",
                LastError = null,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };

            if (proxyConfig is not null)
            {
                runtime.ConfigurationCiphertext = crypto.Encrypt(
                    JsonSerializer.Serialize(
                        new IntegrationWorkerRuntimeConfiguration(
                            proxyConfig,
                            mailConfig is null ? null : ToAccountsManagerMailConfig(mailConfig)),
                        RuntimeJson.Defaults));
                runtime.ConfigurationUpdatedAtUtc = now;
            }

            dbContext.ProjectIntegrationWorkerRuntimes.Add(runtime);

            if (autoProvision)
            {
                await QueueWorkerRuntimeOutboxOperationAsync(
                    dbContext,
                    projectId,
                    normalizedIntegrationKey,
                    runtime.RuntimeAccountId,
                    operation: "provision",
                    nextAttemptAtUtc: now,
                    suppressProvisionOperations: false,
                    suppressDeprovisionOperations: true,
                    ct);
            }

            await dbContext.SaveChangesAsync(ct);
            return new IdempotentExecutionResult(
                StatusCodes.Status201Created,
                new ProjectIntegrationInstanceResponse(
                    httpContext.GetOrCreateRequestId(),
                    ToProjectIntegrationInstanceDto(runtime)));
        },
        cancellationToken);
});

external.MapPatch("/projects/{projectId:guid}/integrations/{integrationKey}/instances/{instanceId:guid}", async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    Guid instanceId,
    Dictionary<string, JsonElement>? request,
    CoreDbContext dbContext,
    ProjectSecretCrypto crypto,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);

    var normalizedIntegrationKey = NormalizeIntegrationKey(integrationKey);
    if (!IntegrationKeys.WorkerIntegrations.Contains(normalizedIntegrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Instances поддержаны только для worker-интеграций.");
    }

    var payload = request ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    var requestedDisplayName = TryReadString(payload, "displayName");
    var makeDefault = payload.TryGetValue("makeDefault", out var makeDefaultValue)
        ? ReadBoolValue(makeDefaultValue, "makeDefault")
        : false;
    var proxyConfig = ReadProxyConfig(payload, "proxyConfig", required: false);
    var mailConfigRequested = payload.ContainsKey("mailConfig");
    var mailConfig = mailConfigRequested ? ReadMailConfig(payload, "mailConfig") : null;

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:integrationInstancesUpdate:{projectId}:{normalizedIntegrationKey}:{instanceId}",
        idempotencyKey,
        async ct =>
        {
            var runtime = await dbContext.ProjectIntegrationWorkerRuntimes
                .SingleOrDefaultAsync(
                    x => x.ProjectId == projectId
                         && x.IntegrationKey == normalizedIntegrationKey
                         && x.Id == instanceId,
                    ct);
            if (runtime is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Integration instance не найден.");
            }

            if (!string.IsNullOrWhiteSpace(requestedDisplayName))
            {
                runtime.InstanceDisplayName = requestedDisplayName;
            }

            if (makeDefault && !runtime.IsDefault)
            {
                var siblings = await dbContext.ProjectIntegrationWorkerRuntimes
                    .Where(x => x.ProjectId == projectId && x.IntegrationKey == normalizedIntegrationKey && x.Id != instanceId)
                    .ToListAsync(ct);
                foreach (var sibling in siblings)
                {
                    sibling.IsDefault = false;
                }

                runtime.IsDefault = true;
            }

            if (proxyConfig is not null || mailConfigRequested)
            {
                var existing = ReadWorkerRuntimeConfiguration(runtime, crypto);
                var effectiveProxy = proxyConfig ?? existing?.ProxyConfig;
                if (effectiveProxy is null)
                {
                    throw new ApiErrorException(
                        StatusCodes.Status400BadRequest,
                        ApiErrorCodes.ValidationError,
                        "Для обновления mailConfig требуется существующий proxyConfig или proxyConfig в payload.");
                }

                var effectiveMail = mailConfigRequested
                    ? (mailConfig is null ? null : ToAccountsManagerMailConfig(mailConfig))
                    : existing?.MailConfig;

                runtime.ConfigurationCiphertext = crypto.Encrypt(
                    JsonSerializer.Serialize(
                        new IntegrationWorkerRuntimeConfiguration(effectiveProxy, effectiveMail),
                        RuntimeJson.Defaults));
                runtime.ConfigurationUpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            runtime.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new ProjectIntegrationInstanceResponse(
                    httpContext.GetOrCreateRequestId(),
                    ToProjectIntegrationInstanceDto(runtime)));
        },
        cancellationToken);
});

external.MapDelete("/projects/{projectId:guid}/integrations/{integrationKey}/instances/{instanceId:guid}", async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    Guid instanceId,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);

    var normalizedIntegrationKey = NormalizeIntegrationKey(integrationKey);
    if (!IntegrationKeys.WorkerIntegrations.Contains(normalizedIntegrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Instances поддержаны только для worker-интеграций.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:integrationInstancesDelete:{projectId}:{normalizedIntegrationKey}:{instanceId}",
        idempotencyKey,
        async ct =>
        {
            var runtime = await dbContext.ProjectIntegrationWorkerRuntimes
                .SingleOrDefaultAsync(
                    x => x.ProjectId == projectId
                         && x.IntegrationKey == normalizedIntegrationKey
                         && x.Id == instanceId,
                    ct);
            if (runtime is null)
            {
                return new IdempotentExecutionResult(
                    StatusCodes.Status200OK,
                    new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
            }

            var now = DateTimeOffset.UtcNow;
            await SupersedeWorkerRuntimeOutboxOperationsAsync(
                dbContext,
                projectId,
                normalizedIntegrationKey,
                runtime.RuntimeAccountId,
                operation: "provision",
                now,
                note: "Suppressed by instance delete.",
                ct);

            runtime.Status = "revoking";
            runtime.LastError = null;
            runtime.UpdatedAtUtc = now;
            await QueueWorkerRuntimeOutboxOperationAsync(
                dbContext,
                projectId,
                normalizedIntegrationKey,
                runtime.RuntimeAccountId,
                operation: "deprovision",
                nextAttemptAtUtc: now,
                suppressProvisionOperations: true,
                suppressDeprovisionOperations: false,
                ct);

            if (runtime.IsDefault)
            {
                var nextDefault = await dbContext.ProjectIntegrationWorkerRuntimes
                    .Where(x => x.ProjectId == projectId
                                && x.IntegrationKey == normalizedIntegrationKey
                                && x.Id != runtime.Id)
                    .OrderBy(x => x.CreatedAtUtc)
                    .FirstOrDefaultAsync(ct);
                if (nextDefault is not null)
                {
                    runtime.IsDefault = false;
                    nextDefault.IsDefault = true;
                }
            }

            await dbContext.SaveChangesAsync(ct);
            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "queued"));
        },
        cancellationToken);
});

external.MapPost("/projects/{projectId:guid}/integrations/{integrationKey}/instances/{instanceId:guid}/runtime/{operation}", async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    Guid instanceId,
    string operation,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);

    var normalizedIntegrationKey = NormalizeIntegrationKey(integrationKey);
    if (!IntegrationKeys.WorkerIntegrations.Contains(normalizedIntegrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Runtime операции поддержаны только для worker-интеграций.");
    }

    var normalizedOperation = operation.Trim().ToLowerInvariant();
    if (normalizedOperation is not ("provision" or "deprovision" or "restart"))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "operation должен быть provision, deprovision или restart.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:integrationInstanceRuntime:{projectId}:{normalizedIntegrationKey}:{instanceId}:{normalizedOperation}",
        idempotencyKey,
        async ct =>
        {
            var runtime = await dbContext.ProjectIntegrationWorkerRuntimes
                .SingleOrDefaultAsync(
                    x => x.ProjectId == projectId
                         && x.IntegrationKey == normalizedIntegrationKey
                         && x.Id == instanceId,
                    ct);
            if (runtime is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Integration instance не найден.");
            }

            var now = DateTimeOffset.UtcNow;
            if (normalizedOperation == "deprovision")
            {
                await SupersedeWorkerRuntimeOutboxOperationsAsync(
                    dbContext,
                    projectId,
                    normalizedIntegrationKey,
                    runtime.RuntimeAccountId,
                    operation: "provision",
                    now,
                    note: "Suppressed by instance runtime deprovision.",
                    ct);
                runtime.Status = "revoking";
                runtime.LastError = null;
                runtime.UpdatedAtUtc = now;
                await QueueWorkerRuntimeOutboxOperationAsync(
                    dbContext,
                    projectId,
                    normalizedIntegrationKey,
                    runtime.RuntimeAccountId,
                    operation: "deprovision",
                    nextAttemptAtUtc: now,
                    suppressProvisionOperations: true,
                    suppressDeprovisionOperations: false,
                    ct);
            }
            else
            {
                var grant = await dbContext.ProjectIntegrationGrants
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        x => x.ProjectId == projectId
                             && x.IntegrationKey == normalizedIntegrationKey
                             && x.Status == "active",
                        ct);
                if (grant is null)
                {
                    throw new ApiErrorException(StatusCodes.Status409Conflict, ApiErrorCodes.Conflict, "Для runtime операции требуется активный grant.");
                }

                runtime.Status = "pending_provision";
                runtime.LastError = null;
                runtime.UpdatedAtUtc = now;
                runtime.DeprovisionedAtUtc = null;

                if (normalizedOperation == "restart" &&
                    (runtime.ProvisionedAtUtc is not null || string.Equals(runtime.Status, "active", StringComparison.OrdinalIgnoreCase)))
                {
                    await QueueWorkerRuntimeOutboxOperationAsync(
                        dbContext,
                        projectId,
                        normalizedIntegrationKey,
                        runtime.RuntimeAccountId,
                        operation: "deprovision",
                        nextAttemptAtUtc: now,
                        suppressProvisionOperations: true,
                        suppressDeprovisionOperations: false,
                        ct);
                }

                await QueueWorkerRuntimeOutboxOperationAsync(
                    dbContext,
                    projectId,
                    normalizedIntegrationKey,
                    runtime.RuntimeAccountId,
                    operation: "provision",
                    nextAttemptAtUtc: normalizedOperation == "restart" ? now.AddSeconds(1) : now,
                    suppressProvisionOperations: false,
                    suppressDeprovisionOperations: false,
                    ct);
            }

            await dbContext.SaveChangesAsync(ct);
            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "queued"));
        },
        cancellationToken);
});

external.MapPost("/projects/{projectId:guid}/integrations/{integrationKey}/instances/{instanceId:guid}/actions/{scope}", async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    Guid instanceId,
    string scope,
    Dictionary<string, JsonElement>? request,
    CoreDbContext dbContext,
    ProjectServiceIntegrationRegistry integrationRegistry,
    IGatewayProxyClient gatewayProxyClient,
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
    if (!IntegrationKeys.WorkerIntegrations.Contains(normalizedIntegrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Instance actions поддержаны только для worker-интеграций.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:integrationInstanceInvoke:{projectId}:{normalizedIntegrationKey}:{instanceId}:{normalizedScope}",
        idempotencyKey,
        async _ =>
        {
            var runtime = await dbContext.ProjectIntegrationWorkerRuntimes
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.ProjectId == projectId
                         && x.IntegrationKey == normalizedIntegrationKey
                         && x.Id == instanceId,
                    cancellationToken);
            if (runtime is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Integration instance не найден.");
            }

            var action = $"ext.integration.{ResolveIntegrationActionNamespace(normalizedIntegrationKey)}.{normalizedScope}";
            var payload = request is null
                ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                : request.ToDictionary(x => x.Key, x => x.Value.Clone(), StringComparer.Ordinal);
            payload["runtimeAccountId"] = JsonSerializer.SerializeToElement(runtime.RuntimeAccountId);

            var authorizationHeader = httpContext.Request.Headers.Authorization.ToString();
            var resultElement = await InvokeProjectIntegrationActionAsync(
                dbContext,
                integrationRegistry,
                gatewayProxyClient,
                entitlementCheckClient,
                crypto,
                projectId,
                actorId,
                normalizedIntegrationKey,
                normalizedScope,
                payload,
                action,
                authorizationHeader,
                idempotencyKey,
                cancellationToken);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new ProxyResponse(httpContext.GetOrCreateRequestId(), resultElement));
        },
        cancellationToken);
});

external.MapPost("/projects/{projectId:guid}/integrations/{integrationKey}/instances/{instanceId:guid}/ui/session", async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    Guid instanceId,
    CoreDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);

    var normalizedIntegrationKey = NormalizeIntegrationKey(integrationKey);
    if (!IntegrationKeys.WorkerIntegrations.Contains(normalizedIntegrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Remote UI поддержан только для worker-интеграций.");
    }

    var runtime = await dbContext.ProjectIntegrationWorkerRuntimes
        .AsNoTracking()
        .SingleOrDefaultAsync(
            x => x.ProjectId == projectId
                 && x.IntegrationKey == normalizedIntegrationKey
                 && x.Id == instanceId,
            cancellationToken);
    if (runtime is null)
    {
        throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Integration instance не найден.");
    }

    var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15);
    var tokenPayload = new Dictionary<string, object?>
    {
        ["projectId"] = projectId,
        ["integrationKey"] = normalizedIntegrationKey,
        ["instanceId"] = instanceId,
        ["runtimeAccountId"] = runtime.RuntimeAccountId,
        ["exp"] = expiresAtUtc.ToUnixTimeSeconds(),
    };
    var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tokenPayload)));
    var iframeUrl = $"/projects/{projectId}/integrations/{normalizedIntegrationKey}/{instanceId}?uiToken={Uri.EscapeDataString(token)}";

    return Results.Ok(new ProjectIntegrationUiSessionResponse(
        httpContext.GetOrCreateRequestId(),
        token,
        expiresAtUtc,
        iframeUrl));
});

app.MapGet("/projects/{projectId:guid}/integrations/{integrationKey}/{instanceId:guid}", async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    Guid instanceId,
    CoreDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IOptions<IntegrationEmbeddedUiOptions> uiOptions,
    CancellationToken cancellationToken) =>
{
    var uiTokenRaw = httpContext.Request.Query["uiToken"].ToString();
    if (!TryParseIntegrationUiToken(uiTokenRaw, out var uiToken))
    {
        throw new ApiErrorException(StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthorized, "uiToken отсутствует или невалиден.");
    }

    if (uiToken.ExpiresAtUtc <= DateTimeOffset.UtcNow)
    {
        throw new ApiErrorException(StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthorized, "uiToken истёк. Обновите iframe-сессию.");
    }

    var normalizedIntegrationKey = NormalizeIntegrationKey(integrationKey);
    if (!string.Equals(uiToken.IntegrationKey, normalizedIntegrationKey, StringComparison.OrdinalIgnoreCase)
        || uiToken.ProjectId != projectId
        || uiToken.InstanceId != instanceId)
    {
        throw new ApiErrorException(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "uiToken не совпадает с запрошенным integration instance.");
    }

    if (!IntegrationKeys.WorkerIntegrations.Contains(normalizedIntegrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Embedded UI поддержан только для worker-интеграций.");
    }

    var runtime = await dbContext.ProjectIntegrationWorkerRuntimes
        .AsNoTracking()
        .SingleOrDefaultAsync(
            x => x.ProjectId == projectId
                 && x.IntegrationKey == normalizedIntegrationKey
                 && x.Id == instanceId,
            cancellationToken);
    if (runtime is null)
    {
        throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Integration instance не найден.");
    }

    if (uiToken.RuntimeAccountId.HasValue && uiToken.RuntimeAccountId.Value != runtime.RuntimeAccountId)
    {
        throw new ApiErrorException(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "uiToken содержит несоответствующий runtimeAccountId.");
    }

    var upstreamBaseUrl = ResolveIntegrationEmbeddedUiBaseUrl(uiOptions.Value, normalizedIntegrationKey);
    var upstreamPath = ResolveIntegrationEmbeddedUiPath(normalizedIntegrationKey, null);
    var upstreamBuilder = new UriBuilder(new Uri(new Uri(upstreamBaseUrl.TrimEnd('/')), upstreamPath))
    {
        Query = string.Join(
            "&",
            new[]
            {
                $"projectId={Uri.EscapeDataString(projectId.ToString("D"))}",
                $"instanceId={Uri.EscapeDataString(instanceId.ToString("D"))}",
                $"runtimeAccountId={Uri.EscapeDataString(runtime.RuntimeAccountId.ToString("D"))}",
            }),
    };

    var workerServiceToken = configuration["WORKER_API_SERVICE_AUTH_CLIENT_TOKEN"];
    if (string.IsNullOrWhiteSpace(workerServiceToken))
    {
        throw new ApiErrorException(
            StatusCodes.Status503ServiceUnavailable,
            ApiErrorCodes.InternalError,
            "Не задан WORKER_API_SERVICE_AUTH_CLIENT_TOKEN для embedded UI proxy.");
    }

    var upstreamRequest = new HttpRequestMessage(HttpMethod.Get, upstreamBuilder.Uri);
    upstreamRequest.Headers.TryAddWithoutValidation(HeaderNames.ServiceToken, workerServiceToken);

    var acceptHeader = httpContext.Request.Headers.Accept.ToString();
    if (!string.IsNullOrWhiteSpace(acceptHeader))
    {
        upstreamRequest.Headers.TryAddWithoutValidation("Accept", acceptHeader);
    }

    var proxyClient = httpClientFactory.CreateClient("integration-ui-proxy");
    var upstreamResponse = await proxyClient.SendAsync(upstreamRequest, cancellationToken);
    var contentType = upstreamResponse.Content.Headers.ContentType?.ToString()
                      ?? "text/plain; charset=utf-8";
    var body = await upstreamResponse.Content.ReadAsStringAsync(cancellationToken);

    return Results.Content(body, contentType, Encoding.UTF8, (int)upstreamResponse.StatusCode);
});

app.MapGet("/projects/{projectId:guid}/integrations/{integrationKey}/{instanceId:guid}/{**embeddedPath}", async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    Guid instanceId,
    string embeddedPath,
    CoreDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IOptions<IntegrationEmbeddedUiOptions> uiOptions,
    CancellationToken cancellationToken) =>
{
    var uiTokenRaw = httpContext.Request.Query["uiToken"].ToString();
    if (!TryParseIntegrationUiToken(uiTokenRaw, out var uiToken))
    {
        throw new ApiErrorException(StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthorized, "uiToken отсутствует или невалиден.");
    }

    if (uiToken.ExpiresAtUtc <= DateTimeOffset.UtcNow)
    {
        throw new ApiErrorException(StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthorized, "uiToken истёк. Обновите iframe-сессию.");
    }

    var normalizedIntegrationKey = NormalizeIntegrationKey(integrationKey);
    if (!string.Equals(uiToken.IntegrationKey, normalizedIntegrationKey, StringComparison.OrdinalIgnoreCase)
        || uiToken.ProjectId != projectId
        || uiToken.InstanceId != instanceId)
    {
        throw new ApiErrorException(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "uiToken не совпадает с запрошенным integration instance.");
    }

    if (!IntegrationKeys.WorkerIntegrations.Contains(normalizedIntegrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Embedded UI поддержан только для worker-интеграций.");
    }

    var runtime = await dbContext.ProjectIntegrationWorkerRuntimes
        .AsNoTracking()
        .SingleOrDefaultAsync(
            x => x.ProjectId == projectId
                 && x.IntegrationKey == normalizedIntegrationKey
                 && x.Id == instanceId,
            cancellationToken);
    if (runtime is null)
    {
        throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Integration instance не найден.");
    }

    if (uiToken.RuntimeAccountId.HasValue && uiToken.RuntimeAccountId.Value != runtime.RuntimeAccountId)
    {
        throw new ApiErrorException(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "uiToken содержит несоответствующий runtimeAccountId.");
    }

    var upstreamBaseUrl = ResolveIntegrationEmbeddedUiBaseUrl(uiOptions.Value, normalizedIntegrationKey);
    var upstreamPath = ResolveIntegrationEmbeddedUiPath(normalizedIntegrationKey, embeddedPath);
    var forwardQueryPairs = new List<string>
    {
        $"projectId={Uri.EscapeDataString(projectId.ToString("D"))}",
        $"instanceId={Uri.EscapeDataString(instanceId.ToString("D"))}",
        $"runtimeAccountId={Uri.EscapeDataString(runtime.RuntimeAccountId.ToString("D"))}",
    };

    foreach (var item in httpContext.Request.Query)
    {
        if (string.Equals(item.Key, "uiToken", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        foreach (var value in item.Value)
        {
            forwardQueryPairs.Add($"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(value ?? string.Empty)}");
        }
    }

    var upstreamBuilder = new UriBuilder(new Uri(new Uri(upstreamBaseUrl.TrimEnd('/')), upstreamPath))
    {
        Query = string.Join("&", forwardQueryPairs),
    };

    var workerServiceToken = configuration["WORKER_API_SERVICE_AUTH_CLIENT_TOKEN"];
    if (string.IsNullOrWhiteSpace(workerServiceToken))
    {
        throw new ApiErrorException(
            StatusCodes.Status503ServiceUnavailable,
            ApiErrorCodes.InternalError,
            "Не задан WORKER_API_SERVICE_AUTH_CLIENT_TOKEN для embedded UI proxy.");
    }

    var upstreamRequest = new HttpRequestMessage(HttpMethod.Get, upstreamBuilder.Uri);
    upstreamRequest.Headers.TryAddWithoutValidation(HeaderNames.ServiceToken, workerServiceToken);

    var acceptHeader = httpContext.Request.Headers.Accept.ToString();
    if (!string.IsNullOrWhiteSpace(acceptHeader))
    {
        upstreamRequest.Headers.TryAddWithoutValidation("Accept", acceptHeader);
    }

    var proxyClient = httpClientFactory.CreateClient("integration-ui-proxy");
    var upstreamResponse = await proxyClient.SendAsync(upstreamRequest, cancellationToken);
    var contentType = upstreamResponse.Content.Headers.ContentType?.ToString()
                      ?? "text/plain; charset=utf-8";
    var body = await upstreamResponse.Content.ReadAsStringAsync(cancellationToken);

    return Results.Content(body, contentType, Encoding.UTF8, (int)upstreamResponse.StatusCode);
});

app.MapMethods("/projects/{projectId:guid}/integrations/{integrationKey}/{instanceId:guid}/{**embeddedPath}", ["POST", "PUT", "PATCH", "DELETE"], async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    Guid instanceId,
    string embeddedPath,
    CoreDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IOptions<IntegrationEmbeddedUiOptions> uiOptions,
    CancellationToken cancellationToken) =>
{
    var uiTokenRaw = httpContext.Request.Query["uiToken"].ToString();
    if (!TryParseIntegrationUiToken(uiTokenRaw, out var uiToken))
    {
        throw new ApiErrorException(StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthorized, "uiToken отсутствует или невалиден.");
    }

    if (uiToken.ExpiresAtUtc <= DateTimeOffset.UtcNow)
    {
        throw new ApiErrorException(StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthorized, "uiToken истёк. Обновите iframe-сессию.");
    }

    var normalizedIntegrationKey = NormalizeIntegrationKey(integrationKey);
    if (!string.Equals(uiToken.IntegrationKey, normalizedIntegrationKey, StringComparison.OrdinalIgnoreCase)
        || uiToken.ProjectId != projectId
        || uiToken.InstanceId != instanceId)
    {
        throw new ApiErrorException(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "uiToken не совпадает с запрошенным integration instance.");
    }

    if (!IntegrationKeys.WorkerIntegrations.Contains(normalizedIntegrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Embedded UI поддержан только для worker-интеграций.");
    }

    var runtime = await dbContext.ProjectIntegrationWorkerRuntimes
        .AsNoTracking()
        .SingleOrDefaultAsync(
            x => x.ProjectId == projectId
                 && x.IntegrationKey == normalizedIntegrationKey
                 && x.Id == instanceId,
            cancellationToken);
    if (runtime is null)
    {
        throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Integration instance не найден.");
    }

    if (uiToken.RuntimeAccountId.HasValue && uiToken.RuntimeAccountId.Value != runtime.RuntimeAccountId)
    {
        throw new ApiErrorException(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "uiToken содержит несоответствующий runtimeAccountId.");
    }

    var upstreamBaseUrl = ResolveIntegrationEmbeddedUiBaseUrl(uiOptions.Value, normalizedIntegrationKey);
    var upstreamPath = ResolveIntegrationEmbeddedUiPath(normalizedIntegrationKey, embeddedPath);
    var forwardQueryPairs = new List<string>
    {
        $"projectId={Uri.EscapeDataString(projectId.ToString("D"))}",
        $"instanceId={Uri.EscapeDataString(instanceId.ToString("D"))}",
        $"runtimeAccountId={Uri.EscapeDataString(runtime.RuntimeAccountId.ToString("D"))}",
    };

    foreach (var item in httpContext.Request.Query)
    {
        if (string.Equals(item.Key, "uiToken", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        foreach (var value in item.Value)
        {
            forwardQueryPairs.Add($"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(value ?? string.Empty)}");
        }
    }

    var upstreamBuilder = new UriBuilder(new Uri(new Uri(upstreamBaseUrl.TrimEnd('/')), upstreamPath))
    {
        Query = string.Join("&", forwardQueryPairs),
    };

    var workerServiceToken = configuration["WORKER_API_SERVICE_AUTH_CLIENT_TOKEN"];
    if (string.IsNullOrWhiteSpace(workerServiceToken))
    {
        throw new ApiErrorException(
            StatusCodes.Status503ServiceUnavailable,
            ApiErrorCodes.InternalError,
            "Не задан WORKER_API_SERVICE_AUTH_CLIENT_TOKEN для embedded UI proxy.");
    }

    var upstreamRequest = new HttpRequestMessage(new HttpMethod(httpContext.Request.Method), upstreamBuilder.Uri);
    upstreamRequest.Headers.TryAddWithoutValidation(HeaderNames.ServiceToken, workerServiceToken);

    var acceptHeader = httpContext.Request.Headers.Accept.ToString();
    if (!string.IsNullOrWhiteSpace(acceptHeader))
    {
        upstreamRequest.Headers.TryAddWithoutValidation("Accept", acceptHeader);
    }

    if ((httpContext.Request.ContentLength ?? 0) > 0 || httpContext.Request.Headers.ContainsKey("Transfer-Encoding"))
    {
        var content = new StreamContent(httpContext.Request.Body);
        if (!string.IsNullOrWhiteSpace(httpContext.Request.ContentType))
        {
            content.Headers.TryAddWithoutValidation("Content-Type", httpContext.Request.ContentType);
        }

        upstreamRequest.Content = content;
    }

    var proxyClient = httpClientFactory.CreateClient("integration-ui-proxy");
    var upstreamResponse = await proxyClient.SendAsync(upstreamRequest, cancellationToken);
    var contentType = upstreamResponse.Content.Headers.ContentType?.ToString()
                      ?? "text/plain; charset=utf-8";
    var body = await upstreamResponse.Content.ReadAsStringAsync(cancellationToken);

    return Results.Content(body, contentType, Encoding.UTF8, (int)upstreamResponse.StatusCode);
});

external.MapPost("/projects/{projectId:guid}/integrations/{integrationKey}/runtime/provision", async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);

    var normalizedIntegrationKey = NormalizeIntegrationKey(integrationKey);
    if (!IntegrationKeys.WorkerIntegrations.Contains(normalizedIntegrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Runtime provision поддержан только для worker-интеграций.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:projectIntegrationRuntimeProvision:{projectId}:{normalizedIntegrationKey}",
        idempotencyKey,
        async ct =>
        {
            var grant = await dbContext.ProjectIntegrationGrants
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IntegrationKey == normalizedIntegrationKey, ct);
            if (grant is null || grant.Status != "active")
            {
                throw new ApiErrorException(StatusCodes.Status409Conflict, ApiErrorCodes.Conflict, "Для provision требуется активный grant.");
            }

            var now = DateTimeOffset.UtcNow;
            var runtime = await LoadDefaultWorkerRuntimeAsync(dbContext, projectId, normalizedIntegrationKey, ct, tracking: true);
            runtime = EnsureWorkerRuntime(
                dbContext,
                runtime,
                projectId,
                normalizedIntegrationKey,
                now,
                status: "pending_provision",
                clearDeprovisionedAt: true);
            await QueueWorkerRuntimeOutboxOperationAsync(
                dbContext,
                projectId,
                normalizedIntegrationKey,
                runtime.RuntimeAccountId,
                operation: "provision",
                now,
                suppressProvisionOperations: false,
                suppressDeprovisionOperations: true,
                ct);

            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "queued"));
        },
        cancellationToken);
});

external.MapPost("/projects/{projectId:guid}/integrations/{integrationKey}/runtime/deprovision", async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);

    var normalizedIntegrationKey = NormalizeIntegrationKey(integrationKey);
    if (!IntegrationKeys.WorkerIntegrations.Contains(normalizedIntegrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Runtime deprovision поддержан только для worker-интеграций.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:projectIntegrationRuntimeDeprovision:{projectId}:{normalizedIntegrationKey}",
        idempotencyKey,
        async ct =>
        {
            var runtime = await LoadDefaultWorkerRuntimeAsync(dbContext, projectId, normalizedIntegrationKey, ct, tracking: true);

            if (runtime is null)
            {
                return new IdempotentExecutionResult(
                    StatusCodes.Status200OK,
                    new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
            }

            var now = DateTimeOffset.UtcNow;
            await SupersedeWorkerRuntimeOutboxOperationsAsync(
                dbContext,
                projectId,
                normalizedIntegrationKey,
                runtime.RuntimeAccountId,
                operation: "provision",
                now,
                note: "Suppressed by project deprovision.",
                ct);
            runtime.Status = "revoking";
            runtime.LastError = null;
            runtime.UpdatedAtUtc = now;
            await QueueWorkerRuntimeOutboxOperationAsync(
                dbContext,
                projectId,
                normalizedIntegrationKey,
                runtime.RuntimeAccountId,
                operation: "deprovision",
                now,
                suppressProvisionOperations: true,
                suppressDeprovisionOperations: false,
                ct);

            await dbContext.SaveChangesAsync(ct);
            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "queued"));
        },
        cancellationToken);
});

external.MapPost("/projects/{projectId:guid}/integrations/{integrationKey}/runtime/restart", async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);

    var normalizedIntegrationKey = NormalizeIntegrationKey(integrationKey);
    if (!IntegrationKeys.WorkerIntegrations.Contains(normalizedIntegrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Runtime restart поддержан только для worker-интеграций.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:projectIntegrationRuntimeRestart:{projectId}:{normalizedIntegrationKey}",
        idempotencyKey,
        async ct =>
        {
            var grant = await dbContext.ProjectIntegrationGrants
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IntegrationKey == normalizedIntegrationKey, ct);
            if (grant is null || grant.Status != "active")
            {
                throw new ApiErrorException(StatusCodes.Status409Conflict, ApiErrorCodes.Conflict, "Для restart требуется активный grant.");
            }

            var now = DateTimeOffset.UtcNow;
            var runtime = await LoadDefaultWorkerRuntimeAsync(dbContext, projectId, normalizedIntegrationKey, ct, tracking: true);
            var hadActiveRuntime = runtime?.ProvisionedAtUtc is not null || string.Equals(runtime?.Status, "active", StringComparison.OrdinalIgnoreCase);
            runtime = EnsureWorkerRuntime(
                dbContext,
                runtime,
                projectId,
                normalizedIntegrationKey,
                now,
                status: "pending_provision",
                clearDeprovisionedAt: true);
            if (hadActiveRuntime)
            {
                await QueueWorkerRuntimeOutboxOperationAsync(
                    dbContext,
                    projectId,
                    normalizedIntegrationKey,
                    runtime.RuntimeAccountId,
                    operation: "deprovision",
                    now,
                    suppressProvisionOperations: true,
                    suppressDeprovisionOperations: false,
                    ct);
            }
            await QueueWorkerRuntimeOutboxOperationAsync(
                dbContext,
                projectId,
                normalizedIntegrationKey,
                runtime.RuntimeAccountId,
                operation: "provision",
                now.AddSeconds(1),
                suppressProvisionOperations: false,
                suppressDeprovisionOperations: false,
                ct);

            await dbContext.SaveChangesAsync(ct);
            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "queued"));
        },
        cancellationToken);
});

external.MapPost("/projects/{projectId:guid}/integrations/{integrationKey}/runtime/configure", async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    Dictionary<string, JsonElement> request,
    CoreDbContext dbContext,
    ProjectSecretCrypto crypto,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);

    var normalizedIntegrationKey = NormalizeIntegrationKey(integrationKey);
    if (!IntegrationKeys.WorkerIntegrations.Contains(normalizedIntegrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Runtime configure поддержан только для worker-интеграций.");
    }

    var proxyConfig = ReadProxyConfig(request, "proxyConfig", required: true)!;
    var mailConfigRequested = request.ContainsKey("mailConfig");
    var mailConfig = mailConfigRequested ? ReadMailConfig(request, "mailConfig") : null;

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:projectIntegrationRuntimeConfigure:{projectId}:{normalizedIntegrationKey}",
        idempotencyKey,
        async ct =>
        {
            var now = DateTimeOffset.UtcNow;
            var grant = await dbContext.ProjectIntegrationGrants
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IntegrationKey == normalizedIntegrationKey, ct);
            var scopes = NormalizeScopes(normalizedIntegrationKey, requestedScopes: null);
            if (grant is null)
            {
                grant = new ProjectIntegrationGrantEntity
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    IntegrationKey = normalizedIntegrationKey,
                    Status = "active",
                    ScopesCsv = string.Join(',', scopes),
                    MaxInstances = 1,
                    GrantedByUserId = actorId,
                    GrantedAtUtc = now,
                };
                dbContext.ProjectIntegrationGrants.Add(grant);
            }
            else
            {
                grant.Status = "active";
                grant.ScopesCsv = string.Join(',', scopes);
                grant.MaxInstances = Math.Max(1, grant.MaxInstances);
                grant.GrantedByUserId = actorId;
                grant.GrantedAtUtc = now;
                grant.RevokedByUserId = null;
                grant.RevokedAtUtc = null;
            }

            var legacyCredential = await dbContext.ProjectServiceCredentials
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IntegrationKey == normalizedIntegrationKey, ct);
            if (legacyCredential is not null)
            {
                legacyCredential.Status = "revoked";
                legacyCredential.RevokedAtUtc = now;
                legacyCredential.UpdatedAtUtc = now;
            }

            var runtime = await LoadDefaultWorkerRuntimeAsync(dbContext, projectId, normalizedIntegrationKey, ct, tracking: true);
            var existingRuntimeConfig = ReadWorkerRuntimeConfiguration(runtime, crypto);
            runtime = EnsureWorkerRuntime(
                dbContext,
                runtime,
                projectId,
                normalizedIntegrationKey,
                now,
                status: "pending_provision",
                clearDeprovisionedAt: true);

            var requestedMailConfig = mailConfigRequested && mailConfig is not null
                ? ToAccountsManagerMailConfig(mailConfig)
                : null;
            var effectiveMailConfig = mailConfigRequested
                ? requestedMailConfig
                : existingRuntimeConfig?.MailConfig;
            runtime.ConfigurationCiphertext = crypto.Encrypt(
                JsonSerializer.Serialize(
                    new IntegrationWorkerRuntimeConfiguration(proxyConfig, effectiveMailConfig),
                    RuntimeJson.Defaults));
            runtime.ConfigurationUpdatedAtUtc = now;

            await QueueWorkerRuntimeOutboxOperationAsync(
                dbContext,
                projectId,
                normalizedIntegrationKey,
                runtime.RuntimeAccountId,
                operation: "provision",
                now,
                suppressProvisionOperations: false,
                suppressDeprovisionOperations: true,
                ct);

            dbContext.NotificationOutbox.Add(CreateNotificationOutbox(
                projectId,
                "integration.runtime.configured",
                $"Интеграция `{normalizedIntegrationKey}` настроена и поставлена на provision.",
                $"{projectId:N}:{normalizedIntegrationKey}:runtime-configure:{idempotencyKey}"));

            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "queued"));
        },
        cancellationToken);
});

external.MapGet("/projects/{projectId:guid}/integrations/steam/accounts", async (
    HttpContext httpContext,
    Guid projectId,
    string? query,
    string? status,
    int? page,
    int? pageSize,
    CoreDbContext dbContext,
    ProjectServiceIntegrationRegistry integrationRegistry,
    IGatewayProxyClient gatewayProxyClient,
    IEntitlementCheckClient entitlementCheckClient,
    ProjectSecretCrypto crypto,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);

    var authorizationHeader = httpContext.Request.Headers.Authorization.ToString();
    var payload = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
    {
        ["operation"] = JsonSerializer.SerializeToElement("accounts.list"),
        ["page"] = JsonSerializer.SerializeToElement(Math.Max(1, page ?? 1)),
        ["pageSize"] = JsonSerializer.SerializeToElement(Math.Clamp(pageSize ?? 50, 1, 200)),
    };
    if (!string.IsNullOrWhiteSpace(query))
    {
        payload["query"] = JsonSerializer.SerializeToElement(query.Trim());
    }

    if (!string.IsNullOrWhiteSpace(status))
    {
        payload["status"] = JsonSerializer.SerializeToElement(status.Trim());
    }

    var result = await InvokeProjectIntegrationActionAsync(
        dbContext,
        integrationRegistry,
        gatewayProxyClient,
        entitlementCheckClient,
        crypto,
        projectId,
        actorId,
        IntegrationKeys.SteamAccountsManager,
        scope: "read",
        request: payload,
        action: "ext.integration.steam.read",
        authorizationHeader,
        idempotencyKey: $"steam-accounts-list:{projectId:N}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
        cancellationToken);

    var items = ReadRequiredProperty<SteamIntegrationAccountDto[]>(
        result,
        "items",
        "Steam accounts list");
    var totalCount = ReadOptionalIntProperty(result, "totalCount") ?? items.Length;

    return Results.Ok(new SteamIntegrationAccountsResponse(
        httpContext.GetOrCreateRequestId(),
        items,
        totalCount));
});

external.MapPost("/projects/{projectId:guid}/integrations/steam/accounts", async (
    HttpContext httpContext,
    Guid projectId,
    SteamIntegrationAccountUpsertRequest request,
    CoreDbContext dbContext,
    ProjectServiceIntegrationRegistry integrationRegistry,
    IGatewayProxyClient gatewayProxyClient,
    IEntitlementCheckClient entitlementCheckClient,
    ProjectSecretCrypto crypto,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    var authorizationHeader = httpContext.Request.Headers.Authorization.ToString();

    if (string.IsNullOrWhiteSpace(request.LoginName))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "loginName обязателен.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:steamIntegrationAccountCreate:{projectId}",
        idempotencyKey,
        async _ =>
        {
            var payload = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["operation"] = JsonSerializer.SerializeToElement("accounts.create"),
                ["account"] = JsonSerializer.SerializeToElement(request),
            };

            var result = await InvokeProjectIntegrationActionAsync(
                dbContext,
                integrationRegistry,
                gatewayProxyClient,
                entitlementCheckClient,
                crypto,
                projectId,
                actorId,
                IntegrationKeys.SteamAccountsManager,
                scope: "jobs",
                request: payload,
                action: "ext.integration.steam.jobs",
                authorizationHeader,
                idempotencyKey,
                cancellationToken);
            var account = ReadRequiredProperty<SteamIntegrationAccountDto>(
                result,
                "account",
                "Steam account create");

            return new IdempotentExecutionResult(
                StatusCodes.Status201Created,
                new SteamIntegrationAccountResponse(httpContext.GetOrCreateRequestId(), account));
        },
        cancellationToken);
});

external.MapPatch("/projects/{projectId:guid}/integrations/steam/accounts/{accountId:guid}", async (
    HttpContext httpContext,
    Guid projectId,
    Guid accountId,
    SteamIntegrationAccountUpsertRequest request,
    CoreDbContext dbContext,
    ProjectServiceIntegrationRegistry integrationRegistry,
    IGatewayProxyClient gatewayProxyClient,
    IEntitlementCheckClient entitlementCheckClient,
    ProjectSecretCrypto crypto,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    var authorizationHeader = httpContext.Request.Headers.Authorization.ToString();

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:steamIntegrationAccountUpdate:{projectId}:{accountId}",
        idempotencyKey,
        async _ =>
        {
            var payload = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["operation"] = JsonSerializer.SerializeToElement("accounts.update"),
                ["accountId"] = JsonSerializer.SerializeToElement(accountId),
                ["account"] = JsonSerializer.SerializeToElement(request),
            };

            var result = await InvokeProjectIntegrationActionAsync(
                dbContext,
                integrationRegistry,
                gatewayProxyClient,
                entitlementCheckClient,
                crypto,
                projectId,
                actorId,
                IntegrationKeys.SteamAccountsManager,
                scope: "jobs",
                request: payload,
                action: "ext.integration.steam.jobs",
                authorizationHeader,
                idempotencyKey,
                cancellationToken);
            var account = ReadRequiredProperty<SteamIntegrationAccountDto>(
                result,
                "account",
                "Steam account update");

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new SteamIntegrationAccountResponse(httpContext.GetOrCreateRequestId(), account));
        },
        cancellationToken);
});

external.MapDelete("/projects/{projectId:guid}/integrations/steam/accounts/{accountId:guid}", async (
    HttpContext httpContext,
    Guid projectId,
    Guid accountId,
    CoreDbContext dbContext,
    ProjectServiceIntegrationRegistry integrationRegistry,
    IGatewayProxyClient gatewayProxyClient,
    IEntitlementCheckClient entitlementCheckClient,
    ProjectSecretCrypto crypto,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    var authorizationHeader = httpContext.Request.Headers.Authorization.ToString();

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:steamIntegrationAccountArchive:{projectId}:{accountId}",
        idempotencyKey,
        async _state =>
        {
            var payload = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["operation"] = JsonSerializer.SerializeToElement("accounts.archive"),
                ["accountId"] = JsonSerializer.SerializeToElement(accountId),
            };

            _ = await InvokeProjectIntegrationActionAsync(
                dbContext,
                integrationRegistry,
                gatewayProxyClient,
                entitlementCheckClient,
                crypto,
                projectId,
                actorId,
                IntegrationKeys.SteamAccountsManager,
                scope: "jobs",
                request: payload,
                action: "ext.integration.steam.jobs",
                authorizationHeader,
                idempotencyKey,
                cancellationToken);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
        },
        cancellationToken);
});

external.MapGet("/projects/{projectId:guid}/integrations/steam/jobs", async (
    HttpContext httpContext,
    Guid projectId,
    int? take,
    CoreDbContext dbContext,
    ProjectServiceIntegrationRegistry integrationRegistry,
    IGatewayProxyClient gatewayProxyClient,
    IEntitlementCheckClient entitlementCheckClient,
    ProjectSecretCrypto crypto,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);
    var authorizationHeader = httpContext.Request.Headers.Authorization.ToString();

    var payload = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
    {
        ["operation"] = JsonSerializer.SerializeToElement("jobs.list"),
        ["take"] = JsonSerializer.SerializeToElement(Math.Clamp(take ?? 30, 1, 200)),
    };

    var result = await InvokeProjectIntegrationActionAsync(
        dbContext,
        integrationRegistry,
        gatewayProxyClient,
        entitlementCheckClient,
        crypto,
        projectId,
        actorId,
        IntegrationKeys.SteamAccountsManager,
        scope: "read",
        request: payload,
        action: "ext.integration.steam.read",
        authorizationHeader,
        idempotencyKey: $"steam-jobs-list:{projectId:N}:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
        cancellationToken);

    var jobs = ReadRequiredProperty<SteamIntegrationJobDto[]>(
        result,
        "jobs",
        "Steam jobs list");

    return Results.Ok(new SteamIntegrationJobsResponse(
        httpContext.GetOrCreateRequestId(),
        jobs));
});

external.MapPost("/projects/{projectId:guid}/integrations/steam/jobs", async (
    HttpContext httpContext,
    Guid projectId,
    SteamIntegrationJobCreateRequest request,
    CoreDbContext dbContext,
    ProjectServiceIntegrationRegistry integrationRegistry,
    IGatewayProxyClient gatewayProxyClient,
    IEntitlementCheckClient entitlementCheckClient,
    ProjectSecretCrypto crypto,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    var authorizationHeader = httpContext.Request.Headers.Authorization.ToString();

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:steamIntegrationJobCreate:{projectId}",
        idempotencyKey,
        async _ =>
        {
            var payload = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["operation"] = JsonSerializer.SerializeToElement("jobs.create"),
                ["job"] = JsonSerializer.SerializeToElement(request),
            };

            var result = await InvokeProjectIntegrationActionAsync(
                dbContext,
                integrationRegistry,
                gatewayProxyClient,
                entitlementCheckClient,
                crypto,
                projectId,
                actorId,
                IntegrationKeys.SteamAccountsManager,
                scope: "jobs",
                request: payload,
                action: "ext.integration.steam.jobs",
                authorizationHeader,
                idempotencyKey,
                cancellationToken);
            var job = ReadRequiredProperty<SteamIntegrationJobDto>(
                result,
                "job",
                "Steam job create");

            return new IdempotentExecutionResult(
                StatusCodes.Status201Created,
                new SteamIntegrationJobResponse(httpContext.GetOrCreateRequestId(), job));
        },
        cancellationToken);
});

external.MapPost("/projects/{projectId:guid}/integrations/steam/jobs/{jobId:guid}/cancel", async (
    HttpContext httpContext,
    Guid projectId,
    Guid jobId,
    CoreDbContext dbContext,
    ProjectServiceIntegrationRegistry integrationRegistry,
    IGatewayProxyClient gatewayProxyClient,
    IEntitlementCheckClient entitlementCheckClient,
    ProjectSecretCrypto crypto,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsUse, cancellationToken);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    var authorizationHeader = httpContext.Request.Headers.Authorization.ToString();

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:steamIntegrationJobCancel:{projectId}:{jobId}",
        idempotencyKey,
        async _state =>
        {
            var payload = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["operation"] = JsonSerializer.SerializeToElement("jobs.cancel"),
                ["jobId"] = JsonSerializer.SerializeToElement(jobId),
            };

            _ = await InvokeProjectIntegrationActionAsync(
                dbContext,
                integrationRegistry,
                gatewayProxyClient,
                entitlementCheckClient,
                crypto,
                projectId,
                actorId,
                IntegrationKeys.SteamAccountsManager,
                scope: "jobs",
                request: payload,
                action: "ext.integration.steam.jobs",
                authorizationHeader,
                idempotencyKey,
                cancellationToken);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
        },
        cancellationToken);
});

external.MapPost("/projects/{projectId:guid}/integrations/{integrationKey}/actions/{scope}", async (
    HttpContext httpContext,
    Guid projectId,
    string integrationKey,
    string scope,
    Dictionary<string, JsonElement>? request,
    CoreDbContext dbContext,
    ProjectServiceIntegrationRegistry integrationRegistry,
    IGatewayProxyClient gatewayProxyClient,
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
    if (!IntegrationKeys.ServiceIntegrations.Contains(normalizedIntegrationKey)
        && !IntegrationKeys.WorkerIntegrations.Contains(normalizedIntegrationKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Integration не поддерживает actions.");
    }

    var action = $"ext.integration.{ResolveIntegrationActionNamespace(normalizedIntegrationKey)}.{normalizedScope}";

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:integrationInvoke:{projectId}:{normalizedIntegrationKey}:{normalizedScope}",
        idempotencyKey,
        async _ =>
        {
            var authorizationHeader = httpContext.Request.Headers.Authorization.ToString();
            var resultElement = await InvokeProjectIntegrationActionAsync(
                dbContext,
                integrationRegistry,
                gatewayProxyClient,
                entitlementCheckClient,
                crypto,
                projectId,
                actorId,
                normalizedIntegrationKey,
                normalizedScope,
                request,
                action,
                authorizationHeader,
                idempotencyKey,
                cancellationToken);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new ProxyResponse(httpContext.GetOrCreateRequestId(), resultElement));
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

external.MapGet("/projects/{projectId:guid}/integrations/custom-http", async (
    HttpContext httpContext,
    Guid projectId,
    CoreDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    EnsureFeatureEnabled(customHttpFeatureEnabled, "custom-http");
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsCustomManage, cancellationToken);
    await EnsureCustomHttpGrantAsync(dbContext, projectId, cancellationToken);

    var items = await dbContext.ProjectCustomHttpIntegrations
        .AsNoTracking()
        .Where(x => x.ProjectId == projectId)
        .OrderBy(x => x.Name)
        .Select(x => new ProjectCustomHttpIntegrationDto(
            x.Id,
            x.Name,
            x.BaseUrl,
            x.Status,
            x.BearerTokenMasked,
            x.LastTestedAtUtc,
            x.UpdatedAtUtc))
        .ToListAsync(cancellationToken);

    return Results.Ok(new ProjectCustomHttpIntegrationListResponse(
        httpContext.GetOrCreateRequestId(),
        items));
});

external.MapPost("/projects/{projectId:guid}/integrations/custom-http", async (
    HttpContext httpContext,
    Guid projectId,
    ProjectCustomHttpIntegrationCreateRequest request,
    CoreDbContext dbContext,
    ProjectSecretCrypto crypto,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    EnsureFeatureEnabled(customHttpFeatureEnabled, "custom-http");
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsCustomManage, cancellationToken);
    await EnsureCustomHttpGrantAsync(dbContext, projectId, cancellationToken);

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:customHttpIntegrationCreate:{projectId}",
        idempotencyKey,
        async ct =>
        {
            if (string.IsNullOrWhiteSpace(request.Name)
                || string.IsNullOrWhiteSpace(request.BaseUrl)
                || string.IsNullOrWhiteSpace(request.BearerToken))
            {
                throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "name/baseUrl/bearerToken обязательны.");
            }

            var normalizedStatus = NormalizeCustomIntegrationStatus(request.Status);
            var allowlist = await dbContext.AdminCustomHttpAllowlist
                .AsNoTracking()
                .Where(x => x.IsActive)
                .Select(x => x.HostPattern)
                .ToListAsync(ct);
            var uri = new Uri(request.BaseUrl.Trim());
            await CustomHttpIntegrationInvoker.ValidateTargetUriAsync(uri, allowlist, ct);

            var now = DateTimeOffset.UtcNow;
            var entity = new ProjectCustomHttpIntegrationEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Name = request.Name.Trim(),
                BaseUrl = request.BaseUrl.Trim(),
                Status = normalizedStatus,
                BearerTokenCiphertext = crypto.Encrypt(request.BearerToken.Trim()),
                BearerTokenMasked = MaskToken(request.BearerToken.Trim()),
                DefaultHeadersJson = request.DefaultHeaders is null
                    ? null
                    : JsonSerializer.Serialize(request.DefaultHeaders),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };

            dbContext.ProjectCustomHttpIntegrations.Add(entity);
            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status201Created,
                new ProjectCustomHttpIntegrationResponse(
                    httpContext.GetOrCreateRequestId(),
                    new ProjectCustomHttpIntegrationDto(
                        entity.Id,
                        entity.Name,
                        entity.BaseUrl,
                        entity.Status,
                        entity.BearerTokenMasked,
                        entity.LastTestedAtUtc,
                        entity.UpdatedAtUtc)));
        },
        cancellationToken);
});

external.MapPatch("/projects/{projectId:guid}/integrations/custom-http/{integrationId:guid}", async (
    HttpContext httpContext,
    Guid projectId,
    Guid integrationId,
    ProjectCustomHttpIntegrationUpdateRequest request,
    CoreDbContext dbContext,
    ProjectSecretCrypto crypto,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    EnsureFeatureEnabled(customHttpFeatureEnabled, "custom-http");
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsCustomManage, cancellationToken);
    await EnsureCustomHttpGrantAsync(dbContext, projectId, cancellationToken);

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:customHttpIntegrationUpdate:{projectId}:{integrationId}",
        idempotencyKey,
        async ct =>
        {
            var entity = await dbContext.ProjectCustomHttpIntegrations
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == integrationId, ct);
            if (entity is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Кастомная интеграция не найдена.");
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                entity.Name = request.Name.Trim();
            }

            if (!string.IsNullOrWhiteSpace(request.BaseUrl))
            {
                var allowlist = await dbContext.AdminCustomHttpAllowlist
                    .AsNoTracking()
                    .Where(x => x.IsActive)
                    .Select(x => x.HostPattern)
                    .ToListAsync(ct);
                var endpoint = new Uri(request.BaseUrl.Trim());
                await CustomHttpIntegrationInvoker.ValidateTargetUriAsync(endpoint, allowlist, ct);
                entity.BaseUrl = request.BaseUrl.Trim();
            }

            if (!string.IsNullOrWhiteSpace(request.BearerToken))
            {
                var token = request.BearerToken.Trim();
                entity.BearerTokenCiphertext = crypto.Encrypt(token);
                entity.BearerTokenMasked = MaskToken(token);
            }

            if (request.DefaultHeaders is not null)
            {
                entity.DefaultHeadersJson = JsonSerializer.Serialize(request.DefaultHeaders);
            }

            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                entity.Status = NormalizeCustomIntegrationStatus(request.Status);
            }

            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new ProjectCustomHttpIntegrationResponse(
                    httpContext.GetOrCreateRequestId(),
                    new ProjectCustomHttpIntegrationDto(
                        entity.Id,
                        entity.Name,
                        entity.BaseUrl,
                        entity.Status,
                        entity.BearerTokenMasked,
                        entity.LastTestedAtUtc,
                        entity.UpdatedAtUtc)));
        },
        cancellationToken);
});

external.MapDelete("/projects/{projectId:guid}/integrations/custom-http/{integrationId:guid}", async (
    HttpContext httpContext,
    Guid projectId,
    Guid integrationId,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    EnsureFeatureEnabled(customHttpFeatureEnabled, "custom-http");
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsCustomManage, cancellationToken);
    await EnsureCustomHttpGrantAsync(dbContext, projectId, cancellationToken);

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:customHttpIntegrationDelete:{projectId}:{integrationId}",
        idempotencyKey,
        async ct =>
        {
            var entity = await dbContext.ProjectCustomHttpIntegrations
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == integrationId, ct);
            if (entity is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Кастомная интеграция не найдена.");
            }

            dbContext.ProjectCustomHttpIntegrations.Remove(entity);
            await dbContext.SaveChangesAsync(ct);
            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
        },
        cancellationToken);
});

external.MapPost("/projects/{projectId:guid}/integrations/custom-http/{integrationId:guid}/test", async (
    HttpContext httpContext,
    Guid projectId,
    Guid integrationId,
    ProjectCustomHttpIntegrationTestRequest request,
    CoreDbContext dbContext,
    ICustomHttpIntegrationInvoker invoker,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    EnsureFeatureEnabled(customHttpFeatureEnabled, "custom-http");
    var actorId = GetCurrentUserId(httpContext);
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectIntegrationsCustomManage, cancellationToken);
    await EnsureCustomHttpGrantAsync(dbContext, projectId, cancellationToken);

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:customHttpIntegrationTest:{projectId}:{integrationId}",
        idempotencyKey,
        async ct =>
        {
            var result = await invoker.InvokeAsync(
                projectId,
                integrationId,
                new CustomHttpInvokeRequest(
                    request.Method ?? "POST",
                    request.Path,
                    request.Headers,
                    request.Payload is null ? null : JsonSerializer.Serialize(request.Payload)),
                ct);

            var integration = await dbContext.ProjectCustomHttpIntegrations
                .SingleOrDefaultAsync(x => x.Id == integrationId && x.ProjectId == projectId, ct);
            if (integration is not null)
            {
                integration.LastTestedAtUtc = DateTimeOffset.UtcNow;
                integration.UpdatedAtUtc = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(ct);
            }

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new ProjectCustomHttpIntegrationTestResponse(
                    httpContext.GetOrCreateRequestId(),
                    result.StatusCode,
                    result.Endpoint,
                    result.Body));
        },
        cancellationToken);
});

external.MapGet("/projects/{projectId:guid}/offers", async (
    HttpContext httpContext,
    Guid projectId,
    CoreDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    EnsureFeatureEnabled(offersFeatureEnabled, "offers");
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectOffersManage, cancellationToken);

    var offers = await dbContext.Offers
        .AsNoTracking()
        .Where(x => x.ProjectId == projectId)
        .OrderBy(x => x.Name)
        .ToListAsync(cancellationToken);
    var variants = await dbContext.OfferVariants
        .AsNoTracking()
        .Where(x => x.ProjectId == projectId)
        .ToListAsync(cancellationToken);
    var variantsByOffer = variants
        .GroupBy(x => x.OfferId)
        .ToDictionary(group => group.Key, group => group.ToList());

    var items = offers
        .Select(offer =>
        {
            variantsByOffer.TryGetValue(offer.Id, out var offerVariants);
            return ToOfferDto(offer, offerVariants ?? []);
        })
        .ToList();

    return Results.Ok(new OfferListResponse(httpContext.GetOrCreateRequestId(), items));
});

external.MapPost("/projects/{projectId:guid}/offers", async (
    HttpContext httpContext,
    Guid projectId,
    OfferCreateRequest request,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    EnsureFeatureEnabled(offersFeatureEnabled, "offers");
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectOffersManage, cancellationToken);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:offerCreate:{projectId}",
        idempotencyKey,
        async ct =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "offer.name обязателен.");
            }

            var now = DateTimeOffset.UtcNow;
            var offer = new OfferEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                Status = NormalizeOfferStatus(request.Status),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            dbContext.Offers.Add(offer);
            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status201Created,
                new OfferResponse(httpContext.GetOrCreateRequestId(), ToOfferDto(offer, [])));
        },
        cancellationToken);
});

external.MapGet("/projects/{projectId:guid}/offers/{offerId:guid}", async (
    HttpContext httpContext,
    Guid projectId,
    Guid offerId,
    CoreDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    EnsureFeatureEnabled(offersFeatureEnabled, "offers");
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectOffersManage, cancellationToken);

    var offer = await dbContext.Offers
        .AsNoTracking()
        .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == offerId, cancellationToken);
    if (offer is null)
    {
        throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Offer не найден.");
    }

    var variants = await dbContext.OfferVariants
        .AsNoTracking()
        .Where(x => x.ProjectId == projectId && x.OfferId == offerId)
        .OrderBy(x => x.Priority)
        .ToListAsync(cancellationToken);

    return Results.Ok(new OfferResponse(httpContext.GetOrCreateRequestId(), ToOfferDto(offer, variants)));
});

external.MapPatch("/projects/{projectId:guid}/offers/{offerId:guid}", async (
    HttpContext httpContext,
    Guid projectId,
    Guid offerId,
    OfferUpdateRequest request,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    EnsureFeatureEnabled(offersFeatureEnabled, "offers");
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectOffersManage, cancellationToken);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:offerUpdate:{projectId}:{offerId}",
        idempotencyKey,
        async ct =>
        {
            var offer = await dbContext.Offers
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == offerId, ct);
            if (offer is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Offer не найден.");
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                offer.Name = request.Name.Trim();
            }

            if (request.Description is not null)
            {
                offer.Description = request.Description.Trim();
            }

            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                offer.Status = NormalizeOfferStatus(request.Status);
            }

            offer.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(ct);

            var variants = await dbContext.OfferVariants
                .AsNoTracking()
                .Where(x => x.ProjectId == projectId && x.OfferId == offerId)
                .OrderBy(x => x.Priority)
                .ToListAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new OfferResponse(httpContext.GetOrCreateRequestId(), ToOfferDto(offer, variants)));
        },
        cancellationToken);
});

external.MapDelete("/projects/{projectId:guid}/offers/{offerId:guid}", async (
    HttpContext httpContext,
    Guid projectId,
    Guid offerId,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    EnsureFeatureEnabled(offersFeatureEnabled, "offers");
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectOffersManage, cancellationToken);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:offerDelete:{projectId}:{offerId}",
        idempotencyKey,
        async ct =>
        {
            var offer = await dbContext.Offers
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == offerId, ct);
            if (offer is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Offer не найден.");
            }

            var variants = await dbContext.OfferVariants
                .Where(x => x.ProjectId == projectId && x.OfferId == offerId)
                .ToListAsync(ct);
            dbContext.OfferVariants.RemoveRange(variants);

            var definition = await dbContext.WorkflowDefinitions
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.OfferId == offerId, ct);
            if (definition is not null)
            {
                dbContext.WorkflowDefinitions.Remove(definition);
            }

            dbContext.Offers.Remove(offer);
            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
        },
        cancellationToken);
});

external.MapPut("/projects/{projectId:guid}/offers/{offerId:guid}/variants", async (
    HttpContext httpContext,
    Guid projectId,
    Guid offerId,
    OfferVariantsReplaceRequest request,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    EnsureFeatureEnabled(offersFeatureEnabled, "offers");
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectOffersManage, cancellationToken);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:offerVariantsReplace:{projectId}:{offerId}",
        idempotencyKey,
        async ct =>
        {
            var offer = await dbContext.Offers
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == offerId, ct);
            if (offer is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Offer не найден.");
            }

            var items = request.Items ?? [];
            var accountIds = items.Select(x => x.AccountId).Distinct().ToArray();
            if (accountIds.Length > 0)
            {
                var knownAccountIds = await dbContext.Accounts
                    .AsNoTracking()
                    .Where(x => x.ProjectId == projectId && accountIds.Contains(x.Id))
                    .Select(x => x.Id)
                    .ToListAsync(ct);
                var missing = accountIds.Except(knownAccountIds).ToArray();
                if (missing.Length > 0)
                {
                    throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "В variants есть accountId, не принадлежащий проекту.");
                }
            }

            var existingVariants = await dbContext.OfferVariants
                .Where(x => x.ProjectId == projectId && x.OfferId == offerId)
                .ToListAsync(ct);
            dbContext.OfferVariants.RemoveRange(existingVariants);

            var now = DateTimeOffset.UtcNow;
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.WorkerProductId)
                    || string.IsNullOrWhiteSpace(item.Platform)
                    || string.IsNullOrWhiteSpace(item.ObservedTitle)
                    || string.IsNullOrWhiteSpace(item.ObservedCurrency))
                {
                    throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "variant workerProductId/platform/observedTitle/observedCurrency обязательны.");
                }

                dbContext.OfferVariants.Add(new OfferVariantEntity
                {
                    Id = Guid.NewGuid(),
                    OfferId = offerId,
                    ProjectId = projectId,
                    AccountId = item.AccountId,
                    WorkerProductId = item.WorkerProductId.Trim(),
                    Platform = item.Platform.Trim().ToLowerInvariant(),
                    ObservedTitle = item.ObservedTitle.Trim(),
                    ObservedDescription = item.ObservedDescription?.Trim(),
                    ObservedPrice = item.ObservedPrice,
                    ObservedCurrency = item.ObservedCurrency.Trim().ToUpperInvariant(),
                    Priority = item.Priority,
                    IsActive = item.IsActive,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                });
            }

            offer.UpdatedAtUtc = now;
            await dbContext.SaveChangesAsync(ct);

            var variants = await dbContext.OfferVariants
                .AsNoTracking()
                .Where(x => x.ProjectId == projectId && x.OfferId == offerId)
                .OrderBy(x => x.Priority)
                .ToListAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new OfferResponse(httpContext.GetOrCreateRequestId(), ToOfferDto(offer, variants)));
        },
        cancellationToken);
});

external.MapGet("/projects/{projectId:guid}/offers/{offerId:guid}/workflow/draft", async (
    HttpContext httpContext,
    Guid projectId,
    Guid offerId,
    CoreDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    EnsureFeatureEnabled(workflowsFeatureEnabled, "workflows");
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectWorkflowsManage, cancellationToken);

    var offerExists = await dbContext.Offers.AnyAsync(x => x.ProjectId == projectId && x.Id == offerId, cancellationToken);
    if (!offerExists)
    {
        throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Offer не найден.");
    }

    var definition = await dbContext.WorkflowDefinitions
        .AsNoTracking()
        .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.OfferId == offerId, cancellationToken);

    var draft = definition is null
        ? new WorkflowDraftModel()
        : JsonSerializer.Deserialize<WorkflowDraftModel>(definition.DraftJson, WorkflowExecutionEngine.JsonOptions()) ?? new WorkflowDraftModel();

    return Results.Ok(new WorkflowDraftResponse(
        httpContext.GetOrCreateRequestId(),
        draft,
        definition?.Status ?? "draft",
        definition?.PublishedVersion ?? 0,
        definition?.PublishedAtUtc));
});

external.MapPut("/projects/{projectId:guid}/offers/{offerId:guid}/workflow/draft", async (
    HttpContext httpContext,
    Guid projectId,
    Guid offerId,
    WorkflowDraftModel request,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    EnsureFeatureEnabled(workflowsFeatureEnabled, "workflows");
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectWorkflowsManage, cancellationToken);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:workflowDraftPut:{projectId}:{offerId}",
        idempotencyKey,
        async ct =>
        {
            var offer = await dbContext.Offers
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == offerId, ct);
            if (offer is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Offer не найден.");
            }

            ValidateWorkflowDraftForSave(request);

            var now = DateTimeOffset.UtcNow;
            var definition = await dbContext.WorkflowDefinitions
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.OfferId == offerId, ct);
            if (definition is null)
            {
                definition = new WorkflowDefinitionEntity
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    OfferId = offerId,
                    DraftJson = JsonSerializer.Serialize(request),
                    PublishedJson = null,
                    Status = "draft",
                    PublishedVersion = 0,
                    MaxSteps = request.MaxSteps,
                    MaxDurationSeconds = request.MaxDurationSeconds,
                    MaxRetries = request.MaxRetries,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    UpdatedByUserId = actorId,
                };
                dbContext.WorkflowDefinitions.Add(definition);
            }
            else
            {
                definition.DraftJson = JsonSerializer.Serialize(request);
                definition.MaxSteps = request.MaxSteps;
                definition.MaxDurationSeconds = request.MaxDurationSeconds;
                definition.MaxRetries = request.MaxRetries;
                definition.UpdatedAtUtc = now;
                definition.UpdatedByUserId = actorId;
                if (string.IsNullOrWhiteSpace(definition.PublishedJson))
                {
                    definition.Status = "draft";
                }
            }

            await dbContext.SaveChangesAsync(ct);
            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new WorkflowDraftResponse(
                    httpContext.GetOrCreateRequestId(),
                    request,
                    definition.Status,
                    definition.PublishedVersion,
                    definition.PublishedAtUtc));
        },
        cancellationToken);
});

external.MapPost("/projects/{projectId:guid}/offers/{offerId:guid}/workflow/publish", async (
    HttpContext httpContext,
    Guid projectId,
    Guid offerId,
    CoreDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    EnsureFeatureEnabled(workflowsFeatureEnabled, "workflows");
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectWorkflowsManage, cancellationToken);
    var idempotencyKey = httpContext.RequireIdempotencyKey();

    return await idempotency.ExecuteAsync(
        dbContext,
        $"core:workflowPublish:{projectId}:{offerId}",
        idempotencyKey,
        async ct =>
        {
            var definition = await dbContext.WorkflowDefinitions
                .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.OfferId == offerId, ct);
            if (definition is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Workflow draft не найден.");
            }

            var draft = JsonSerializer.Deserialize<WorkflowDraftModel>(definition.DraftJson, WorkflowExecutionEngine.JsonOptions())
                ?? throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Workflow draft поврежден.");
            WorkflowGraphValidator.ValidateOrThrow(draft);

            definition.PublishedJson = definition.DraftJson;
            definition.Status = "published";
            definition.PublishedVersion = Math.Max(1, definition.PublishedVersion + 1);
            definition.PublishedAtUtc = DateTimeOffset.UtcNow;
            definition.PublishedByUserId = actorId;
            definition.UpdatedAtUtc = DateTimeOffset.UtcNow;
            definition.UpdatedByUserId = actorId;

            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new WorkflowDraftResponse(
                    httpContext.GetOrCreateRequestId(),
                    draft,
                    definition.Status,
                    definition.PublishedVersion,
                    definition.PublishedAtUtc));
        },
        cancellationToken);
});

external.MapGet("/projects/{projectId:guid}/offers/{offerId:guid}/workflow/executions", async (
    HttpContext httpContext,
    Guid projectId,
    Guid offerId,
    CoreDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    EnsureFeatureEnabled(workflowsFeatureEnabled, "workflows");
    var actorId = GetCurrentUserId(httpContext);
    await EnsurePermissionAsync(dbContext, projectId, actorId, ProjectPermissions.ProjectWorkflowsManage, cancellationToken);

    var executions = await dbContext.WorkflowExecutions
        .AsNoTracking()
        .Where(x => x.ProjectId == projectId && x.OfferId == offerId)
        .OrderByDescending(x => x.StartedAtUtc)
        .Take(50)
        .ToListAsync(cancellationToken);
    var executionIds = executions.Select(x => x.Id).ToArray();
    var steps = await dbContext.WorkflowExecutionSteps
        .AsNoTracking()
        .Where(x => executionIds.Contains(x.ExecutionId))
        .OrderBy(x => x.StepIndex)
        .ToListAsync(cancellationToken);
    var stepsByExecutionId = steps
        .GroupBy(x => x.ExecutionId)
        .ToDictionary(group => group.Key, group => group.ToList());

    var items = executions.Select(execution =>
    {
        stepsByExecutionId.TryGetValue(execution.Id, out var executionSteps);
        return new WorkflowExecutionDto(
            execution.Id,
            execution.SourceOrderId,
            execution.WorkflowVersion,
            execution.Status,
            execution.StartedAtUtc,
            execution.FinishedAtUtc,
            execution.LastError,
            executionSteps?
                .Select(step => new WorkflowExecutionStepDto(
                    step.NodeId,
                    step.NodeType,
                    step.StepIndex,
                    step.Status,
                    step.StartedAtUtc,
                    step.FinishedAtUtc,
                    step.OutputJson,
                    step.Error))
                .ToList() ?? []);
    }).ToList();

    return Results.Ok(new WorkflowExecutionListResponse(httpContext.GetOrCreateRequestId(), items));
});

app.MapPost("/v1/integrations/workflow/purchase", async (
    HttpContext httpContext,
    PurchaseWebhookRequest request,
    CoreDbContext dbContext,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    EnsureFeatureEnabled(workflowsFeatureEnabled, "workflows");
    var workflowPurchaseWebhookSecret = configuration["WORKFLOW_PURCHASE_WEBHOOK_SECRET"] ?? string.Empty;

    if (string.IsNullOrWhiteSpace(workflowPurchaseWebhookSecret)
        || !httpContext.Request.Headers.TryGetValue("X-Workflow-Purchase-Secret", out var providedSecret)
        || !string.Equals(providedSecret.ToString(), workflowPurchaseWebhookSecret, StringComparison.Ordinal))
    {
        throw new ApiErrorException(StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthorized, "Некорректный webhook secret.");
    }

    if (request.ProjectId == Guid.Empty || request.OfferId == Guid.Empty || string.IsNullOrWhiteSpace(request.SourceOrderId))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "projectId/offerId/sourceOrderId обязательны.");
    }

    var offerExists = await dbContext.Offers.AnyAsync(
        x => x.ProjectId == request.ProjectId && x.Id == request.OfferId,
        cancellationToken);
    if (!offerExists)
    {
        throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Offer не найден.");
    }

    var hasPublishedWorkflow = await dbContext.WorkflowDefinitions.AnyAsync(
        x => x.ProjectId == request.ProjectId
             && x.OfferId == request.OfferId
             && !string.IsNullOrWhiteSpace(x.PublishedJson),
        cancellationToken);
    if (!hasPublishedWorkflow)
    {
        throw new ApiErrorException(StatusCodes.Status409Conflict, ApiErrorCodes.Conflict, "Для offer не опубликован workflow.");
    }

    var normalizedSourceOrderId = request.SourceOrderId.Trim();
    var normalizedEventType = NormalizeWorkflowEventType(request.EventType);
    var triggerSource = BuildWorkflowTriggerSource(normalizedEventType);
    var duplicate = await dbContext.WorkflowTriggerEvents.AnyAsync(
        x => x.ProjectId == request.ProjectId
             && x.SourceOrderId == normalizedSourceOrderId
             && x.Source == triggerSource,
        cancellationToken);
    if (duplicate)
    {
        return Results.Ok(new PurchaseWebhookAckResponse(
            httpContext.GetOrCreateRequestId(),
            "duplicate"));
    }

    var now = DateTimeOffset.UtcNow;
    var payload = request.Payload is null
        ? new Dictionary<string, object?>(StringComparer.Ordinal)
        : new Dictionary<string, object?>(request.Payload, StringComparer.Ordinal);

    if (!string.IsNullOrWhiteSpace(request.Platform))
    {
        payload["platform"] = request.Platform.Trim();
    }

    if (request.Quantity.HasValue)
    {
        payload["quantity"] = request.Quantity.Value;
    }

    if (request.Amount.HasValue)
    {
        payload["amount"] = request.Amount.Value;
    }

    if (!string.IsNullOrWhiteSpace(request.Currency))
    {
        payload["currency"] = request.Currency.Trim().ToUpperInvariant();
    }

    if (!string.IsNullOrWhiteSpace(request.MessageText))
    {
        payload["messageText"] = request.MessageText.Trim();
    }

    if (request.ReviewRating.HasValue)
    {
        payload["reviewRating"] = request.ReviewRating.Value;
    }

    if (!string.IsNullOrWhiteSpace(request.ReviewText))
    {
        payload["reviewText"] = request.ReviewText.Trim();
    }

    var triggerEvent = new WorkflowTriggerEventEntity
    {
        Id = Guid.NewGuid(),
        ProjectId = request.ProjectId,
        OfferId = request.OfferId,
        Source = triggerSource,
        SourceOrderId = normalizedSourceOrderId,
        BuyerId = request.BuyerId?.Trim(),
        PayloadJson = payload.Count == 0 ? "{}" : JsonSerializer.Serialize(payload),
        Status = "accepted",
        CreatedAtUtc = now,
    };
    dbContext.WorkflowTriggerEvents.Add(triggerEvent);
    dbContext.WorkflowOutbox.Add(new WorkflowOutboxEntity
    {
        Id = Guid.NewGuid(),
        TriggerEventId = triggerEvent.Id,
        ProjectId = request.ProjectId,
        OfferId = request.OfferId,
        Status = "pending",
        AttemptCount = 0,
        NextAttemptAtUtc = now,
        CreatedAtUtc = now,
    });
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Accepted(
        $"/v1/projects/{request.ProjectId}/offers/{request.OfferId}/workflow/executions",
        new PurchaseWebhookAckResponse(
            httpContext.GetOrCreateRequestId(),
            "accepted"));
}).AllowAnonymous();

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
    var mailConfig = ReadMailConfig(request, "mailConfig");
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

    if (mailConfig is not null && !string.Equals(platform, "steam", StringComparison.OrdinalIgnoreCase))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Поле mailConfig поддерживается только для platform=steam.");
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
                    mailConfig is null ? null : ToAccountsManagerMailConfig(mailConfig),
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
                mailConfig: null,
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
        || action.StartsWith("ext.account.marketplace-auth.", StringComparison.Ordinal)
        || action.StartsWith("ext.integration.", StringComparison.Ordinal))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Для ext.account.* и ext.integration.* используйте профильные endpoint-ы Core API.");
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

static MailConfigPayload? ReadMailConfig(Dictionary<string, JsonElement> payload, string key)
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
    var enabled = objectValue.TryGetValue("enabled", out var enabledValue)
        ? ReadBoolValue(enabledValue, "mailConfig.enabled")
        : true;

    if (!enabled)
    {
        return new MailConfigPayload(
            Enabled: false,
            ImapHost: string.Empty,
            ImapPort: 0,
            ImapSecurity: "ssl",
            ImapUsername: string.Empty,
            ImapPassword: string.Empty,
            Mailbox: null,
            SearchFrom: null,
            SearchSubject: null);
    }

    var host = ReadString(objectValue, "imapHost");
    var username = ReadString(objectValue, "imapUsername");
    var password = ReadString(objectValue, "imapPassword");

    var port = objectValue.TryGetValue("imapPort", out var portValue)
        ? ReadPortValue(portValue, "mailConfig.imapPort")
        : 993;

    var security = objectValue.TryGetValue("imapSecurity", out var securityValue)
        ? ReadStringValue(securityValue, "mailConfig.imapSecurity").Trim().ToLowerInvariant()
        : "ssl";
    if (!MailConfigImapSecurityModes.All.Contains(security))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Поле mailConfig.imapSecurity содержит неподдерживаемое значение.");
    }

    var mailbox = TryReadOptionalStringValue(objectValue, "mailbox");
    var searchFrom = TryReadOptionalStringValue(objectValue, "searchFrom");
    var searchSubject = TryReadOptionalStringValue(objectValue, "searchSubject");

    return new MailConfigPayload(
        Enabled: true,
        ImapHost: host,
        ImapPort: port,
        ImapSecurity: security,
        ImapUsername: username,
        ImapPassword: password,
        Mailbox: mailbox,
        SearchFrom: searchFrom,
        SearchSubject: searchSubject);
}

static bool ReadBoolValue(JsonElement value, string fieldName)
{
    if (value.ValueKind == JsonValueKind.True)
    {
        return true;
    }

    if (value.ValueKind == JsonValueKind.False)
    {
        return false;
    }

    if (value.ValueKind == JsonValueKind.String &&
        bool.TryParse(value.GetString(), out var parsed))
    {
        return parsed;
    }

    throw new ApiErrorException(
        StatusCodes.Status400BadRequest,
        ApiErrorCodes.ValidationError,
        $"Поле {fieldName} должно быть bool-значением.");
}

static int ReadPortValue(JsonElement value, string fieldName)
{
    var port = value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetInt32(out var intPort) => intPort,
        JsonValueKind.String when int.TryParse(value.GetString(), out var stringPort) => stringPort,
        _ => throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            $"Поле {fieldName} должно быть числом."),
    };

    if (port is < 1 or > 65535)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            $"Поле {fieldName} должно быть в диапазоне 1..65535.");
    }

    return port;
}

static string ReadStringValue(JsonElement value, string fieldName)
{
    if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            $"Поле {fieldName} должно быть непустой строкой.");
    }

    return value.GetString()!.Trim();
}

static string? TryReadOptionalStringValue(Dictionary<string, JsonElement> payload, string key)
{
    if (!payload.TryGetValue(key, out var value))
    {
        return null;
    }

    if (value.ValueKind == JsonValueKind.Null)
    {
        return null;
    }

    return ReadStringValue(value, $"mailConfig.{key}");
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

static AccountsManagerMailConfig ToAccountsManagerMailConfig(MailConfigPayload payload)
{
    return new AccountsManagerMailConfig(
        payload.Enabled,
        payload.ImapHost,
        payload.ImapPort,
        payload.ImapSecurity,
        payload.ImapUsername,
        payload.ImapPassword,
        payload.Mailbox,
        payload.SearchFrom,
        payload.SearchSubject);
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

static OfferDto ToOfferDto(OfferEntity offer, IReadOnlyCollection<OfferVariantEntity> variants)
{
    var activeVariants = variants
        .Where(x => x.IsActive)
        .ToList();

    var prices = activeVariants
        .Select(x => x.ObservedPrice)
        .ToArray();
    var currencies = activeVariants
        .Select(x => x.ObservedCurrency.Trim().ToUpperInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(x => x, StringComparer.Ordinal)
        .ToList();
    decimal? minPrice = prices.Length == 0 ? null : prices.Min();
    decimal? maxPrice = prices.Length == 0 ? null : prices.Max();
    decimal? averagePrice = prices.Length == 0 ? null : decimal.Round(prices.Average(), 2);

    return new OfferDto(
        offer.Id,
        offer.Name,
        offer.Description,
        offer.Status,
        minPrice,
        maxPrice,
        averagePrice,
        currencies,
        variants.Count,
        variants
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.CreatedAtUtc)
            .Select(x => new OfferVariantDto(
                x.Id,
                x.AccountId,
                x.WorkerProductId,
                x.Platform,
                x.ObservedTitle,
                x.ObservedDescription,
                x.ObservedPrice,
                x.ObservedCurrency,
                x.Priority,
                x.IsActive))
            .ToList(),
        offer.CreatedAtUtc,
        offer.UpdatedAtUtc);
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

static ProjectIntegrationInstanceDto ToProjectIntegrationInstanceDto(ProjectIntegrationWorkerRuntimeEntity runtime)
{
    return new ProjectIntegrationInstanceDto(
        runtime.Id,
        runtime.IntegrationKey,
        runtime.InstanceDisplayName,
        runtime.IsDefault,
        runtime.RuntimeAccountId,
        runtime.Status,
        runtime.LastError,
        runtime.ConfigurationUpdatedAtUtc,
        runtime.ProvisionedAtUtc,
        runtime.DeprovisionedAtUtc,
        runtime.CreatedAtUtc,
        runtime.UpdatedAtUtc);
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

static bool TryParseIntegrationUiToken(string rawToken, out IntegrationUiTokenClaims token)
{
    token = default;

    if (string.IsNullOrWhiteSpace(rawToken))
    {
        return false;
    }

    try
    {
        var payloadJson = Encoding.UTF8.GetString(Convert.FromBase64String(rawToken.Trim()));
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;

        if (!root.TryGetProperty("projectId", out var projectIdElement)
            || projectIdElement.ValueKind != JsonValueKind.String
            || !Guid.TryParse(projectIdElement.GetString(), out var projectId))
        {
            return false;
        }

        if (!root.TryGetProperty("integrationKey", out var integrationKeyElement)
            || integrationKeyElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var integrationKey = integrationKeyElement.GetString();
        if (string.IsNullOrWhiteSpace(integrationKey))
        {
            return false;
        }

        if (!root.TryGetProperty("instanceId", out var instanceIdElement)
            || instanceIdElement.ValueKind != JsonValueKind.String
            || !Guid.TryParse(instanceIdElement.GetString(), out var instanceId))
        {
            return false;
        }

        Guid? runtimeAccountId = null;
        if (root.TryGetProperty("runtimeAccountId", out var runtimeAccountIdElement)
            && runtimeAccountIdElement.ValueKind == JsonValueKind.String
            && Guid.TryParse(runtimeAccountIdElement.GetString(), out var parsedRuntimeAccountId))
        {
            runtimeAccountId = parsedRuntimeAccountId;
        }

        if (!root.TryGetProperty("exp", out var expElement))
        {
            return false;
        }

        long expUnix = expElement.ValueKind switch
        {
            JsonValueKind.Number when expElement.TryGetInt64(out var value) => value,
            JsonValueKind.String when long.TryParse(expElement.GetString(), out var value) => value,
            _ => -1,
        };
        if (expUnix <= 0)
        {
            return false;
        }

        token = new IntegrationUiTokenClaims(
            projectId,
            NormalizeIntegrationKey(integrationKey),
            instanceId,
            runtimeAccountId,
            DateTimeOffset.FromUnixTimeSeconds(expUnix));

        return true;
    }
    catch
    {
        return false;
    }
}

static string ResolveIntegrationEmbeddedUiBaseUrl(IntegrationEmbeddedUiOptions options, string integrationKey)
{
    if (string.Equals(integrationKey, IntegrationKeys.SteamAccountsManager, StringComparison.OrdinalIgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(options.SteamAccountsManagerBaseUrl))
        {
            throw new ApiErrorException(
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorCodes.InternalError,
                "Не задан IntegrationEmbeddedUi:SteamAccountsManagerBaseUrl.");
        }

        return options.SteamAccountsManagerBaseUrl;
    }

    throw new ApiErrorException(
        StatusCodes.Status400BadRequest,
        ApiErrorCodes.ValidationError,
        $"Для integration `{integrationKey}` не настроен embedded UI endpoint.");
}

static string ResolveIntegrationEmbeddedUiPath(string integrationKey, string? embeddedPath)
{
    if (!string.Equals(integrationKey, IntegrationKeys.SteamAccountsManager, StringComparison.OrdinalIgnoreCase))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            $"Для integration `{integrationKey}` не поддержан embedded UI path resolver.");
    }

    var pathSuffix = string.IsNullOrWhiteSpace(embeddedPath)
        ? string.Empty
        : $"/{embeddedPath.Trim().Trim('/')}";
    return $"/internal/v2/worker/ui/steam{pathSuffix}";
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
    else if (IntegrationKeys.WorkerIntegrations.Contains(integrationKey))
    {
        allowed = new HashSet<string>(["read", "jobs"], StringComparer.OrdinalIgnoreCase);
        defaults = ["read", "jobs"];
    }
    else if (string.Equals(integrationKey, IntegrationKeys.Telegram, StringComparison.OrdinalIgnoreCase))
    {
        allowed = new HashSet<string>(["send"], StringComparer.OrdinalIgnoreCase);
        defaults = ["send"];
    }
    else if (string.Equals(integrationKey, IntegrationKeys.CustomHttp, StringComparison.OrdinalIgnoreCase))
    {
        allowed = new HashSet<string>(["use"], StringComparer.OrdinalIgnoreCase);
        defaults = ["use"];
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

static string ResolveIntegrationActionNamespace(string integrationKey)
{
    if (string.Equals(integrationKey, IntegrationKeys.SteamAccountsManager, StringComparison.OrdinalIgnoreCase))
    {
        return "steam";
    }

    if (string.Equals(integrationKey, IntegrationKeys.FunPayStat, StringComparison.OrdinalIgnoreCase))
    {
        return "funpaystat";
    }

    return integrationKey.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
}

static IReadOnlyCollection<string> SplitScopes(string scopesCsv)
{
    return scopesCsv
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static ProjectIntegrationWorkerRuntimeEntity EnsureWorkerRuntime(
    CoreDbContext dbContext,
    ProjectIntegrationWorkerRuntimeEntity? runtime,
    Guid projectId,
    string integrationKey,
    DateTimeOffset now,
    string status,
    bool clearDeprovisionedAt)
{
    if (runtime is null)
    {
        runtime = new ProjectIntegrationWorkerRuntimeEntity
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            IntegrationKey = integrationKey,
            InstanceDisplayName = BuildDefaultIntegrationInstanceDisplayName(integrationKey),
            IsDefault = true,
            RuntimeAccountId = CreateDeterministicGuid($"core:integrationRuntime:{projectId}:{integrationKey}"),
            Status = status,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        dbContext.ProjectIntegrationWorkerRuntimes.Add(runtime);
        return runtime;
    }

    runtime.Status = status;
    runtime.LastError = null;
    runtime.IsDefault = true;
    if (string.IsNullOrWhiteSpace(runtime.InstanceDisplayName))
    {
        runtime.InstanceDisplayName = BuildDefaultIntegrationInstanceDisplayName(integrationKey);
    }
    runtime.UpdatedAtUtc = now;
    if (clearDeprovisionedAt)
    {
        runtime.DeprovisionedAtUtc = null;
    }

    return runtime;
}

static int NormalizeIntegrationMaxInstances(string integrationKey, int? requestedMaxInstances)
{
    if (!IntegrationKeys.WorkerIntegrations.Contains(integrationKey))
    {
        return 1;
    }

    var value = requestedMaxInstances ?? 1;
    if (value < 1)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "maxInstances должен быть больше 0.");
    }

    return Math.Clamp(value, 1, 200);
}

static async Task<ProjectIntegrationWorkerRuntimeEntity?> LoadDefaultWorkerRuntimeAsync(
    CoreDbContext dbContext,
    Guid projectId,
    string integrationKey,
    CancellationToken cancellationToken,
    bool tracking)
{
    var query = tracking
        ? dbContext.ProjectIntegrationWorkerRuntimes
        : dbContext.ProjectIntegrationWorkerRuntimes.AsNoTracking();

    return await query
        .Where(x => x.ProjectId == projectId && x.IntegrationKey == integrationKey)
        .OrderByDescending(x => x.IsDefault)
        .ThenBy(x => x.CreatedAtUtc)
        .FirstOrDefaultAsync(cancellationToken);
}

static string BuildDefaultIntegrationInstanceDisplayName(string integrationKey)
{
    return integrationKey switch
    {
        IntegrationKeys.SteamAccountsManager => "Steam runtime (default)",
        _ => $"{integrationKey} runtime (default)",
    };
}

static IntegrationWorkerRuntimeConfiguration? ReadWorkerRuntimeConfiguration(
    ProjectIntegrationWorkerRuntimeEntity? runtime,
    ProjectSecretCrypto crypto)
{
    if (runtime is null || string.IsNullOrWhiteSpace(runtime.ConfigurationCiphertext))
    {
        return null;
    }

    try
    {
        var json = crypto.Decrypt(runtime.ConfigurationCiphertext);
        return JsonSerializer.Deserialize<IntegrationWorkerRuntimeConfiguration>(json, RuntimeJson.Defaults);
    }
    catch
    {
        return null;
    }
}

static async Task QueueWorkerRuntimeOutboxOperationAsync(
    CoreDbContext dbContext,
    Guid projectId,
    string integrationKey,
    Guid runtimeAccountId,
    string operation,
    DateTimeOffset nextAttemptAtUtc,
    bool suppressProvisionOperations,
    bool suppressDeprovisionOperations,
    CancellationToken cancellationToken)
{
    if (suppressProvisionOperations)
    {
        await SupersedeWorkerRuntimeOutboxOperationsAsync(
            dbContext,
            projectId,
            integrationKey,
            runtimeAccountId,
            operation: "provision",
            nextAttemptAtUtc,
            note: $"Suppressed by {operation}.",
            cancellationToken);
    }

    if (suppressDeprovisionOperations)
    {
        await SupersedeWorkerRuntimeOutboxOperationsAsync(
            dbContext,
            projectId,
            integrationKey,
            runtimeAccountId,
            operation: "deprovision",
            nextAttemptAtUtc,
            note: $"Suppressed by {operation}.",
            cancellationToken);
    }

    var pending = await dbContext.IntegrationWorkerRuntimeOutbox
        .SingleOrDefaultAsync(
            x => x.ProjectId == projectId
                 && x.IntegrationKey == integrationKey
                 && x.RuntimeAccountId == runtimeAccountId
                 && x.Operation == operation
                 && (x.Status == "pending" || x.Status == "retry"),
            cancellationToken);
    if (pending is not null)
    {
        pending.Status = "pending";
        pending.AttemptCount = 0;
        pending.NextAttemptAtUtc = nextAttemptAtUtc;
        pending.LastError = null;
        return;
    }

    dbContext.IntegrationWorkerRuntimeOutbox.Add(new IntegrationWorkerRuntimeOutboxEntity
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId,
        IntegrationKey = integrationKey,
        RuntimeAccountId = runtimeAccountId,
        Operation = operation,
        Status = "pending",
        AttemptCount = 0,
        NextAttemptAtUtc = nextAttemptAtUtc,
        CreatedAtUtc = DateTimeOffset.UtcNow,
    });
}

static async Task SupersedeWorkerRuntimeOutboxOperationsAsync(
    CoreDbContext dbContext,
    Guid projectId,
    string integrationKey,
    Guid? runtimeAccountId,
    string operation,
    DateTimeOffset now,
    string note,
    CancellationToken cancellationToken)
{
    var pendingItems = await dbContext.IntegrationWorkerRuntimeOutbox
        .Where(x => x.ProjectId == projectId
                    && x.IntegrationKey == integrationKey
                    && (!runtimeAccountId.HasValue || x.RuntimeAccountId == runtimeAccountId.Value)
                    && x.Operation == operation
                    && (x.Status == "pending" || x.Status == "retry"))
        .ToListAsync(cancellationToken);

    foreach (var item in pendingItems)
    {
        item.Status = "superseded";
        item.ProcessedAtUtc = now;
        item.LastError = note.Length > 1000 ? note[..1000] : note;
    }
}

static TelegramConnectivityFailureInfo ParseTelegramConnectivityFailure(string message)
{
    var normalized = message?.Trim() ?? string.Empty;
    if (normalized.StartsWith("both_failed:", StringComparison.OrdinalIgnoreCase))
    {
        var payload = normalized["both_failed:".Length..].Trim();
        var parts = payload.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var proxyError = parts.FirstOrDefault(x => x.StartsWith("proxy=", StringComparison.OrdinalIgnoreCase));
        var directError = parts.FirstOrDefault(x => x.StartsWith("direct=", StringComparison.OrdinalIgnoreCase));
        return new TelegramConnectivityFailureInfo(
            EffectivePath: "none",
            ProxyAttempted: true,
            ProxySucceeded: false,
            DirectAttempted: true,
            DirectSucceeded: false,
            ReasonCode: "both_failed",
            ProxyError: proxyError?["proxy=".Length..],
            DirectError: directError?["direct=".Length..]);
    }

    if (normalized.StartsWith("proxy_upstream_blocked:", StringComparison.OrdinalIgnoreCase))
    {
        return new TelegramConnectivityFailureInfo(
            EffectivePath: "none",
            ProxyAttempted: true,
            ProxySucceeded: false,
            DirectAttempted: false,
            DirectSucceeded: false,
            ReasonCode: "proxy_upstream_blocked",
            ProxyError: normalized["proxy_upstream_blocked:".Length..].Trim(),
            DirectError: null);
    }

    if (normalized.StartsWith("direct_egress_blocked:", StringComparison.OrdinalIgnoreCase))
    {
        return new TelegramConnectivityFailureInfo(
            EffectivePath: "none",
            ProxyAttempted: false,
            ProxySucceeded: false,
            DirectAttempted: true,
            DirectSucceeded: false,
            ReasonCode: "direct_egress_blocked",
            ProxyError: null,
            DirectError: normalized["direct_egress_blocked:".Length..].Trim());
    }

    if (normalized.StartsWith("telegram_api_rejected:", StringComparison.OrdinalIgnoreCase))
    {
        return new TelegramConnectivityFailureInfo(
            EffectivePath: "none",
            ProxyAttempted: true,
            ProxySucceeded: false,
            DirectAttempted: false,
            DirectSucceeded: false,
            ReasonCode: "telegram_api_rejected",
            ProxyError: normalized["telegram_api_rejected:".Length..].Trim(),
            DirectError: null);
    }

    return new TelegramConnectivityFailureInfo(
        EffectivePath: "none",
        ProxyAttempted: false,
        ProxySucceeded: false,
        DirectAttempted: false,
        DirectSucceeded: false,
        ReasonCode: "unknown",
        ProxyError: normalized,
        DirectError: null);
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

static async Task<TelegramProxyRuntimeConfig?> ResolveActiveTelegramProxyAsync(
    CoreDbContext dbContext,
    ProjectSecretCrypto crypto,
    CancellationToken cancellationToken)
{
    var proxy = await dbContext.TelegramProxyProfiles
        .AsNoTracking()
        .Where(x => x.IsActive)
        .OrderByDescending(x => x.UpdatedAtUtc)
        .FirstOrDefaultAsync(cancellationToken);

    if (proxy is null)
    {
        return null;
    }

    var scheme = string.Equals(proxy.ProxyType, "socks5", StringComparison.OrdinalIgnoreCase) ? "socks5" : "http";
    var uri = new Uri($"{scheme}://{proxy.Host}:{proxy.Port}");
    var login = string.IsNullOrWhiteSpace(proxy.LoginCiphertext) ? null : crypto.Decrypt(proxy.LoginCiphertext);
    var password = string.IsNullOrWhiteSpace(proxy.PasswordCiphertext) ? null : crypto.Decrypt(proxy.PasswordCiphertext);
    return new TelegramProxyRuntimeConfig(uri, login, password);
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

static async Task EnsureCustomHttpGrantAsync(
    CoreDbContext dbContext,
    Guid projectId,
    CancellationToken cancellationToken)
{
    var grant = await dbContext.ProjectIntegrationGrants
        .AsNoTracking()
        .SingleOrDefaultAsync(
            x => x.ProjectId == projectId
                 && x.IntegrationKey == IntegrationKeys.CustomHttp,
            cancellationToken);

    if (grant is null || !string.Equals(grant.Status, "active", StringComparison.OrdinalIgnoreCase))
    {
        throw new ApiErrorException(
            StatusCodes.Status403Forbidden,
            ApiErrorCodes.Forbidden,
            "Для custom HTTP интеграций требуется активный feature-grant `custom-http`.");
    }

    var scopes = SplitScopes(grant.ScopesCsv);
    if (!scopes.Contains("use", StringComparer.OrdinalIgnoreCase))
    {
        throw new ApiErrorException(
            StatusCodes.Status403Forbidden,
            ApiErrorCodes.Forbidden,
            "Для custom HTTP интеграций требуется scope `use`.");
    }
}

static void EnsureFeatureEnabled(bool enabled, string featureName)
{
    if (enabled)
    {
        return;
    }

    throw new ApiErrorException(
        StatusCodes.Status403Forbidden,
        ApiErrorCodes.FeatureNotReady,
        $"Функция `{featureName}` временно выключена feature-flag.");
}

static string NormalizeOfferStatus(string? status)
{
    var normalized = string.IsNullOrWhiteSpace(status)
        ? "active"
        : status.Trim().ToLowerInvariant();
    return normalized switch
    {
        "active" => "active",
        "paused" => "paused",
        "archived" => "archived",
        _ => throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "offer.status должен быть active|paused|archived."),
    };
}

static string NormalizeCustomIntegrationStatus(string? status)
{
    var normalized = string.IsNullOrWhiteSpace(status)
        ? "active"
        : status.Trim().ToLowerInvariant();
    return normalized switch
    {
        "active" => "active",
        "disabled" => "disabled",
        _ => throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "custom integration status должен быть active|disabled."),
    };
}

static string NormalizeAllowlistHostPattern(string? hostPattern)
{
    if (string.IsNullOrWhiteSpace(hostPattern))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "hostPattern обязателен.");
    }

    var normalized = hostPattern.Trim().ToLowerInvariant();
    if (normalized.StartsWith("https://", StringComparison.Ordinal)
        || normalized.StartsWith("http://", StringComparison.Ordinal))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "hostPattern должен содержать только host (без scheme/path).");
    }

    if (normalized.Contains('/') || normalized.Contains(':'))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "hostPattern должен быть в формате host или *.domain.tld.");
    }

    var pattern = normalized;
    if (pattern.StartsWith("*."))
    {
        pattern = pattern[2..];
    }

    if (pattern.Length == 0 || pattern.Contains("..", StringComparison.Ordinal))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Некорректный hostPattern.");
    }

    return normalized;
}

static void ValidateWorkflowDraftForSave(WorkflowDraftModel draft)
{
    if (draft is null)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Workflow draft обязателен.");
    }

    if (string.IsNullOrWhiteSpace(draft.Version))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "workflow.version обязателен.");
    }

    if (draft.MaxSteps is < 1 or > 5000)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "workflow.maxSteps должен быть в диапазоне 1..5000.");
    }

    if (draft.MaxDurationSeconds is < 1 or > 3600)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "workflow.maxDurationSeconds должен быть в диапазоне 1..3600.");
    }

    if (draft.MaxRetries is < 0 or > 20)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "workflow.maxRetries должен быть в диапазоне 0..20.");
    }

    var nodeIds = new HashSet<string>(StringComparer.Ordinal);
    foreach (var node in draft.Nodes)
    {
        if (string.IsNullOrWhiteSpace(node.Id))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "workflow.nodes[].id обязателен.");
        }

        var trimmedId = node.Id.Trim();
        if (!nodeIds.Add(trimmedId))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                $"workflow node id `{trimmedId}` повторяется.");
        }

        if (string.IsNullOrWhiteSpace(node.Type) || !WorkflowNodeTypes.All.Contains(node.Type.Trim()))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                $"workflow node type `{node.Type}` не поддерживается.");
        }
    }

    foreach (var edge in draft.Edges)
    {
        if (string.IsNullOrWhiteSpace(edge.Id))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "workflow.edges[].id обязателен.");
        }

        if (string.IsNullOrWhiteSpace(edge.Source) || !nodeIds.Contains(edge.Source.Trim()))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                $"workflow edge source `{edge.Source}` не найден.");
        }

        if (string.IsNullOrWhiteSpace(edge.Target) || !nodeIds.Contains(edge.Target.Trim()))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                $"workflow edge target `{edge.Target}` не найден.");
        }

        if (!string.IsNullOrWhiteSpace(edge.SourceHandle) && edge.SourceHandle.Trim().Length > 120)
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "workflow.edges[].sourceHandle слишком длинный (максимум 120 символов).");
        }

        if (!string.IsNullOrWhiteSpace(edge.TargetHandle) && edge.TargetHandle.Trim().Length > 120)
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "workflow.edges[].targetHandle слишком длинный (максимум 120 символов).");
        }
    }

    WorkflowGraphValidator.ValidateUiOrThrow(draft);
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

static string NormalizeWorkflowEventType(string? eventType)
{
    if (string.IsNullOrWhiteSpace(eventType))
    {
        return "purchase";
    }

    var normalized = eventType.Trim().ToLowerInvariant();
    return normalized switch
    {
        "purchase" => "purchase",
        "message" => "message",
        "review" => "review",
        _ => "purchase",
    };
}

static string BuildWorkflowTriggerSource(string normalizedEventType)
{
    var eventType = NormalizeWorkflowEventType(normalizedEventType);
    return $"{eventType}-webhook";
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

static async Task<JsonElement> InvokeProjectIntegrationActionAsync(
    CoreDbContext dbContext,
    ProjectServiceIntegrationRegistry integrationRegistry,
    IGatewayProxyClient gatewayProxyClient,
    IEntitlementCheckClient entitlementCheckClient,
    ProjectSecretCrypto crypto,
    Guid projectId,
    Guid actorId,
    string normalizedIntegrationKey,
    string scope,
    IDictionary<string, JsonElement>? request,
    string action,
    string authorizationHeader,
    string idempotencyKey,
    CancellationToken cancellationToken)
{
    var normalizedScope = NormalizeScope(scope);
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

    var entitlementAllowed = await entitlementCheckClient.IsAllowedAsync(projectId, actorId, action, cancellationToken);
    if (!entitlementAllowed)
    {
        throw new ApiErrorException(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "Операция заблокирована entitlement policy.");
    }

    if (IntegrationKeys.ServiceIntegrations.Contains(normalizedIntegrationKey))
    {
        if (!integrationRegistry.TryGet(normalizedIntegrationKey, out var integrationClient))
        {
            throw new ApiErrorException(StatusCodes.Status503ServiceUnavailable, ApiErrorCodes.InternalError, "Integration client не зарегистрирован.");
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

        var payload = request is null
            ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            : request.ToDictionary(x => x.Key, x => x.Value.Clone(), StringComparer.Ordinal);
        payload["projectToken"] = JsonSerializer.SerializeToElement(crypto.Decrypt(credential.SecretCiphertext));
        return await integrationClient.InvokeAsync(projectId, normalizedScope, payload, idempotencyKey, cancellationToken);
    }

    Guid? runtimeAccountIdOverride = null;
    if (request is not null &&
        request.TryGetValue("runtimeAccountId", out var runtimeAccountIdElement))
    {
        if (runtimeAccountIdElement.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(runtimeAccountIdElement.GetString(), out var parsedRuntimeAccountId))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "Поле runtimeAccountId должно быть GUID-строкой.");
        }

        runtimeAccountIdOverride = parsedRuntimeAccountId;
    }

    ProjectIntegrationWorkerRuntimeEntity? runtime;
    if (runtimeAccountIdOverride.HasValue)
    {
        runtime = await dbContext.ProjectIntegrationWorkerRuntimes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ProjectId == projectId
                     && x.IntegrationKey == normalizedIntegrationKey
                     && x.RuntimeAccountId == runtimeAccountIdOverride.Value,
                cancellationToken);
    }
    else
    {
        runtime = await LoadDefaultWorkerRuntimeAsync(
            dbContext,
            projectId,
            normalizedIntegrationKey,
            cancellationToken,
            tracking: false);
    }

    if (runtime is null || runtime.Status != "active")
    {
        throw new ApiErrorException(StatusCodes.Status409Conflict, ApiErrorCodes.Conflict, "Интеграционный runtime ещё не активирован.");
    }

    if (string.IsNullOrWhiteSpace(authorizationHeader))
    {
        throw new ApiErrorException(StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthorized, "Отсутствует заголовок Authorization.");
    }

    var workerPayload = request is null
        ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        : request.ToDictionary(x => x.Key, x => x.Value.Clone(), StringComparer.Ordinal);
    workerPayload.Remove("runtimeAccountId");
    workerPayload["projectId"] = JsonSerializer.SerializeToElement(projectId);

    return await gatewayProxyClient.InvokeAccountApiActionAsync(
        BuildRouteKey(runtime.RuntimeAccountId),
        action,
        workerPayload,
        authorizationHeader,
        idempotencyKey,
        cancellationToken);
}

static T ReadRequiredProperty<T>(JsonElement element, string propertyName, string operation)
{
    if (!element.TryGetProperty(propertyName, out var property))
    {
        throw new ApiErrorException(
            StatusCodes.Status502BadGateway,
            ApiErrorCodes.InternalError,
            $"{operation}: worker-ответ не содержит `{propertyName}`.");
    }

    try
    {
        var value = property.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (value is null)
        {
            throw new InvalidOperationException("null");
        }

        return value;
    }
    catch
    {
        throw new ApiErrorException(
            StatusCodes.Status502BadGateway,
            ApiErrorCodes.InternalError,
            $"{operation}: не удалось десериализовать `{propertyName}`.");
    }
}

static int? ReadOptionalIntProperty(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var property))
    {
        return null;
    }

    return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
        ? value
        : null;
}

static string NormalizeBindingType(string? bindingType)
{
    var normalized = (bindingType ?? "group").Trim().ToLowerInvariant();
    return normalized is "group" or "user"
        ? normalized
        : throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "bindingType должен быть group или user.");
}

static async Task EnsureSuperAdminAccountAsync(
    CoreDbContext dbContext,
    string? email,
    string? password,
    string displayName,
    IReadOnlyCollection<string> systemPermissions)
{
    if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(password))
    {
        return;
    }

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
    {
        throw new InvalidOperationException(
            "Для bootstrap супер-админа задайте обе переменные EXTERNAL_API_SUPER_ADMIN_EMAIL и EXTERNAL_API_SUPER_ADMIN_PASSWORD.");
    }

    var normalizedEmail = NormalizeEmail(email);
    ValidatePassword(password, "EXTERNAL_API_SUPER_ADMIN_PASSWORD");

    var user = await dbContext.AuthUsers.SingleOrDefaultAsync(x => x.EmailNormalized == normalizedEmail);
    if (user is not null)
    {
        return;
    }

    var now = DateTimeOffset.UtcNow;
    var superAdmin = new AuthUserEntity
    {
        Id = Guid.NewGuid(),
        Email = normalizedEmail,
        EmailNormalized = normalizedEmail,
        DisplayName = NormalizeDisplayName(displayName, normalizedEmail),
        PasswordHash = HashPassword(password),
        SystemPermissionsCsv = ComposeSystemPermissionsCsv(systemPermissions),
        ForcePasswordChange = true,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
    };

    dbContext.AuthUsers.Add(superAdmin);
    await dbContext.SaveChangesAsync();
}

static AuthSessionResponse BuildAuthSessionResponse(
    HttpContext httpContext,
    AuthUserEntity user,
    string issuer,
    string audience,
    SigningCredentials signingCredentials,
    int tokenLifetimeMinutes,
    string systemPermissionClaimType)
{
    var now = DateTimeOffset.UtcNow;
    var expiresAtUtc = now.AddMinutes(tokenLifetimeMinutes);
    var permissions = ParseSystemPermissionsCsv(user.SystemPermissionsCsv);
    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new(JwtRegisteredClaimNames.Email, user.Email),
        new(ClaimTypes.Email, user.Email),
        new(ClaimTypes.Name, user.DisplayName),
    };

    if (permissions.Count > 0)
    {
        claims.Add(new Claim(systemPermissionClaimType, string.Join(' ', permissions)));
    }

    var token = new JwtSecurityToken(
        issuer: issuer,
        audience: audience,
        claims: claims,
        notBefore: now.UtcDateTime.AddSeconds(-5),
        expires: expiresAtUtc.UtcDateTime,
        signingCredentials: signingCredentials);

    var jwt = new JwtSecurityTokenHandler().WriteToken(token);

    return new AuthSessionResponse(
        httpContext.GetOrCreateRequestId(),
        jwt,
        expiresAtUtc,
        new AuthSessionUserDto(
            user.Id,
            user.Email,
            user.DisplayName,
            permissions,
            user.ForcePasswordChange,
            "local"));
}

static string NormalizeEmail(string email)
{
    if (string.IsNullOrWhiteSpace(email))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "email обязателен.");
    }

    var normalized = email.Trim().ToLowerInvariant();
    try
    {
        _ = new MailAddress(normalized);
    }
    catch
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "email имеет некорректный формат.");
    }

    return normalized;
}

static string NormalizeDisplayName(string? displayName, string fallbackEmail)
{
    var value = string.IsNullOrWhiteSpace(displayName)
        ? fallbackEmail.Split('@')[0]
        : displayName.Trim();

    if (value.Length > 160)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "displayName не должен быть длиннее 160 символов.");
    }

    return value;
}

static void ValidatePassword(string password, string fieldName)
{
    if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            $"Поле {fieldName} должно содержать минимум 8 символов.");
    }
}

static string HashPassword(string password)
{
    const int iterations = 100_000;
    const int saltSize = 16;
    const int keySize = 32;

    var salt = RandomNumberGenerator.GetBytes(saltSize);
    var hash = Rfc2898DeriveBytes.Pbkdf2(
        password,
        salt,
        iterations,
        HashAlgorithmName.SHA256,
        keySize);

    return $"pbkdf2-sha256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
}

static bool VerifyPassword(string password, string encodedHash)
{
    if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(encodedHash))
    {
        return false;
    }

    var parts = encodedHash.Split('$');
    if (parts.Length != 4 || !string.Equals(parts[0], "pbkdf2-sha256", StringComparison.Ordinal))
    {
        return false;
    }

    if (!int.TryParse(parts[1], out var iterations) || iterations < 10_000)
    {
        return false;
    }

    byte[] salt;
    byte[] expectedHash;
    try
    {
        salt = Convert.FromBase64String(parts[2]);
        expectedHash = Convert.FromBase64String(parts[3]);
    }
    catch
    {
        return false;
    }

    var actualHash = Rfc2898DeriveBytes.Pbkdf2(
        password,
        salt,
        iterations,
        HashAlgorithmName.SHA256,
        expectedHash.Length);

    return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
}

static IReadOnlyCollection<string> ParseSystemPermissionsCsv(string? rawValue)
{
    if (string.IsNullOrWhiteSpace(rawValue))
    {
        return [];
    }

    return rawValue
        .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => value.Trim())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static string ComposeSystemPermissionsCsv(IEnumerable<string> permissions)
{
    return string.Join(
        ',',
        permissions
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));
}

public sealed record AckResponse(string RequestId, string Status);

public sealed record AuthRegisterRequest(string Email, string Password, string? DisplayName);

public sealed record AuthLoginRequest(string Email, string Password);

public sealed record AuthChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record AuthSessionUserDto(
    Guid UserId,
    string Email,
    string DisplayName,
    IReadOnlyCollection<string> SystemPermissions,
    bool RequiresPasswordChange,
    string AuthProvider);

public sealed record AuthSessionResponse(
    string RequestId,
    string Token,
    DateTimeOffset ExpiresAtUtc,
    AuthSessionUserDto User);

public sealed record AuthProviderDto(
    string Provider,
    string DisplayName,
    bool Enabled,
    string Status);

public sealed record AuthProviderListResponse(
    string RequestId,
    IReadOnlyCollection<AuthProviderDto> Items);

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

public sealed record AdminIntegrationGrantUpsertRequest(
    IReadOnlyCollection<string>? Scopes,
    int? MaxInstances);

public sealed record AdminIntegrationGrantDto(
    string IntegrationKey,
    string IntegrationType,
    string Status,
    IReadOnlyCollection<string> Scopes,
    int MaxInstances,
    DateTimeOffset GrantedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    string? CredentialStatus,
    string? CredentialMasked,
    string? RuntimeStatus,
    Guid? RuntimeAccountId,
    string? RuntimeLastError);

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

public sealed record AdminTelegramTestMessageRequest(
    string ChatId,
    string? Message);

public sealed record AdminTelegramTestMessageResponse(
    string RequestId,
    string Status,
    string DeliveryPath,
    string? ReasonCode);

public sealed record AdminTelegramConnectivityResponse(
    string RequestId,
    string Status,
    string EffectivePath,
    bool ProxyAttempted,
    bool ProxySucceeded,
    bool DirectAttempted,
    bool DirectSucceeded,
    string? ReasonCode,
    string? ProxyError,
    string? DirectError,
    string? BotId,
    string? Username,
    string? FirstName);

public sealed record ProjectIntegrationStatusDto(
    string IntegrationKey,
    string IntegrationType,
    string Status,
    IReadOnlyCollection<string> Scopes,
    int MaxInstances,
    string? CredentialStatus,
    string? CredentialMasked,
    string? RuntimeStatus,
    Guid? RuntimeAccountId,
    string? RuntimeLastError);

public sealed record TelegramBindingsSummaryDto(
    int GroupChats,
    int UserDmChats);

public sealed record ProjectIntegrationStatusResponse(
    string RequestId,
    IReadOnlyCollection<ProjectIntegrationStatusDto> Items,
    TelegramBindingsSummaryDto Telegram);

public sealed record ProjectIntegrationInstanceDto(
    Guid InstanceId,
    string IntegrationKey,
    string DisplayName,
    bool IsDefault,
    Guid RuntimeAccountId,
    string RuntimeStatus,
    string? RuntimeLastError,
    DateTimeOffset? ConfigurationUpdatedAtUtc,
    DateTimeOffset? ProvisionedAtUtc,
    DateTimeOffset? DeprovisionedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ProjectIntegrationInstanceListResponse(
    string RequestId,
    string IntegrationKey,
    int MaxInstances,
    IReadOnlyCollection<ProjectIntegrationInstanceDto> Items);

public sealed record ProjectIntegrationInstanceResponse(
    string RequestId,
    ProjectIntegrationInstanceDto Instance);

public sealed record ProjectIntegrationUiSessionResponse(
    string RequestId,
    string Token,
    DateTimeOffset ExpiresAtUtc,
    string IframeUrl);

public sealed class IntegrationEmbeddedUiOptions
{
    public string SteamAccountsManagerBaseUrl { get; set; } = "http://host.docker.internal:8080";
}

public readonly record struct IntegrationUiTokenClaims(
    Guid ProjectId,
    string IntegrationKey,
    Guid InstanceId,
    Guid? RuntimeAccountId,
    DateTimeOffset ExpiresAtUtc);

public sealed record AdminCustomHttpAllowlistUpsertRequest(
    string HostPattern,
    bool IsActive,
    string? Note);

public sealed record AdminCustomHttpAllowlistEntryDto(
    Guid Id,
    string HostPattern,
    bool IsActive,
    string? Note,
    DateTimeOffset UpdatedAtUtc);

public sealed record AdminCustomHttpAllowlistResponse(
    string RequestId,
    AdminCustomHttpAllowlistEntryDto Entry);

public sealed record AdminCustomHttpAllowlistListResponse(
    string RequestId,
    IReadOnlyCollection<AdminCustomHttpAllowlistEntryDto> Items);

public sealed record ProjectCustomHttpIntegrationDto(
    Guid Id,
    string Name,
    string BaseUrl,
    string Status,
    string BearerTokenMasked,
    DateTimeOffset? LastTestedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ProjectCustomHttpIntegrationListResponse(
    string RequestId,
    IReadOnlyCollection<ProjectCustomHttpIntegrationDto> Items);

public sealed record ProjectCustomHttpIntegrationResponse(
    string RequestId,
    ProjectCustomHttpIntegrationDto Integration);

public sealed record ProjectCustomHttpIntegrationCreateRequest(
    string Name,
    string BaseUrl,
    string BearerToken,
    string? Status,
    IReadOnlyDictionary<string, string>? DefaultHeaders);

public sealed record ProjectCustomHttpIntegrationUpdateRequest(
    string? Name,
    string? BaseUrl,
    string? BearerToken,
    string? Status,
    IReadOnlyDictionary<string, string>? DefaultHeaders);

public sealed record ProjectCustomHttpIntegrationTestRequest(
    string? Method,
    string? Path,
    IReadOnlyDictionary<string, string>? Headers,
    IReadOnlyDictionary<string, object?>? Payload);

public sealed record ProjectCustomHttpIntegrationTestResponse(
    string RequestId,
    int StatusCode,
    string Endpoint,
    string? Body);

public sealed record OfferVariantUpsertRequest(
    Guid AccountId,
    string WorkerProductId,
    string Platform,
    string ObservedTitle,
    string? ObservedDescription,
    decimal ObservedPrice,
    string ObservedCurrency,
    int Priority,
    bool IsActive);

public sealed record OfferVariantsReplaceRequest(
    IReadOnlyCollection<OfferVariantUpsertRequest>? Items);

public sealed record OfferCreateRequest(
    string Name,
    string? Description,
    string? Status);

public sealed record OfferUpdateRequest(
    string? Name,
    string? Description,
    string? Status);

public sealed record OfferVariantDto(
    Guid Id,
    Guid AccountId,
    string WorkerProductId,
    string Platform,
    string ObservedTitle,
    string? ObservedDescription,
    decimal ObservedPrice,
    string ObservedCurrency,
    int Priority,
    bool IsActive);

public sealed record OfferDto(
    Guid Id,
    string Name,
    string? Description,
    string Status,
    decimal? MinPrice,
    decimal? MaxPrice,
    decimal? AveragePrice,
    IReadOnlyCollection<string> Currencies,
    int VariantCount,
    IReadOnlyCollection<OfferVariantDto> Variants,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record OfferResponse(
    string RequestId,
    OfferDto Offer);

public sealed record OfferListResponse(
    string RequestId,
    IReadOnlyCollection<OfferDto> Items);

public sealed record WorkflowDraftResponse(
    string RequestId,
    WorkflowDraftModel Draft,
    string Status,
    int PublishedVersion,
    DateTimeOffset? PublishedAtUtc);

public sealed record WorkflowExecutionStepDto(
    string NodeId,
    string NodeType,
    int StepIndex,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? OutputJson,
    string? Error);

public sealed record WorkflowExecutionDto(
    Guid Id,
    string SourceOrderId,
    int WorkflowVersion,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? LastError,
    IReadOnlyCollection<WorkflowExecutionStepDto> Steps);

public sealed record WorkflowExecutionListResponse(
    string RequestId,
    IReadOnlyCollection<WorkflowExecutionDto> Items);

public sealed record PurchaseWebhookRequest(
    Guid ProjectId,
    Guid OfferId,
    string SourceOrderId,
    string? BuyerId,
    string? EventType,
    string? Platform,
    decimal? Quantity,
    decimal? Amount,
    string? Currency,
    string? MessageText,
    int? ReviewRating,
    string? ReviewText,
    IReadOnlyDictionary<string, object?>? Payload);

public sealed record PurchaseWebhookAckResponse(
    string RequestId,
    string Status);

public sealed record SteamIntegrationAccountUpsertRequest(
    string LoginName,
    string? DisplayName,
    string? Email,
    string? EmailLogin,
    string? EmailPassword,
    string? PhoneMasked,
    string? Password,
    string? LoginPassword,
    string? SharedSecret,
    string? IdentitySecret,
    string? GuardRecoveryCode,
    string? MaFilePayload,
    string? AccessToken,
    string? RefreshToken,
    string? AuthSessionId,
    string? SteamLoginSecure,
    string? SteamRememberLogin,
    string? WebCookie,
    string? DeviceId,
    string? MachineName,
    string? FamilyViewPin,
    string? CountryCode,
    string? TimeZone,
    string? SessionPayload,
    string? RecoveryPayload,
    string? SteamId64,
    string? Proxy,
    string? FolderName,
    IReadOnlyDictionary<string, string>? AuthHeaders,
    IReadOnlyCollection<string>? Tags,
    string? Note,
    IReadOnlyDictionary<string, string>? Metadata,
    string? Status);

public sealed record SteamIntegrationAccountDto(
    Guid Id,
    string LoginName,
    string? DisplayName,
    string? SteamId64,
    string? Email,
    string? PhoneMasked,
    string? Proxy,
    string? FolderName,
    string? Status,
    string? Note,
    IReadOnlyCollection<string> Tags,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SteamIntegrationAccountsResponse(
    string RequestId,
    IReadOnlyCollection<SteamIntegrationAccountDto> Items,
    int TotalCount);

public sealed record SteamIntegrationAccountResponse(
    string RequestId,
    SteamIntegrationAccountDto Account);

public sealed record SteamIntegrationJobCreateRequest(
    string Type,
    IReadOnlyCollection<Guid> AccountIds,
    bool DryRun,
    int Parallelism,
    int RetryCount,
    IReadOnlyDictionary<string, string>? Payload);

public sealed record SteamIntegrationJobItemDto(
    Guid Id,
    Guid JobId,
    Guid AccountId,
    string Status,
    int Attempt,
    string? ErrorText,
    string? ReasonCode,
    bool Retryable,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyDictionary<string, string> Request,
    IReadOnlyDictionary<string, string> Result);

public sealed record SteamIntegrationJobDto(
    Guid Id,
    string Type,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int TotalCount,
    int SuccessCount,
    int FailureCount,
    bool DryRun,
    IReadOnlyDictionary<string, string> Payload,
    IReadOnlyCollection<SteamIntegrationJobItemDto>? Items);

public sealed record SteamIntegrationJobsResponse(
    string RequestId,
    IReadOnlyCollection<SteamIntegrationJobDto> Items);

public sealed record SteamIntegrationJobResponse(
    string RequestId,
    SteamIntegrationJobDto Job);

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

internal sealed record TelegramConnectivityFailureInfo(
    string EffectivePath,
    bool ProxyAttempted,
    bool ProxySucceeded,
    bool DirectAttempted,
    bool DirectSucceeded,
    string ReasonCode,
    string? ProxyError,
    string? DirectError);

internal sealed record MarketplaceAuthPayload(string Scheme, IReadOnlyDictionary<string, string> Credentials);

internal sealed record MailConfigPayload(
    bool Enabled,
    string ImapHost,
    int ImapPort,
    string ImapSecurity,
    string ImapUsername,
    string ImapPassword,
    string? Mailbox,
    string? SearchFrom,
    string? SearchSubject);

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

internal static class MailConfigImapSecurityModes
{
    public static readonly HashSet<string> All = new(
        ["ssl", "tls", "implicit_tls", "starttls", "starttls_when_available", "none", "auto"],
        StringComparer.OrdinalIgnoreCase);
}

public partial class Program;

