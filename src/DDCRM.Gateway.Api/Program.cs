using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DDCRM.Gateway.Api.Clients;
using DDCRM.Shared.Authorization;
using DDCRM.Shared.Errors;
using DDCRM.Shared.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RouteRegistryClientOptions>(builder.Configuration.GetSection(RouteRegistryClientOptions.SectionName));
builder.Services.Configure<IamClientOptions>(builder.Configuration.GetSection(IamClientOptions.SectionName));
builder.Services.Configure<EntitlementClientOptions>(builder.Configuration.GetSection(EntitlementClientOptions.SectionName));
builder.Services.Configure<WorkerProxyClientOptions>(builder.Configuration.GetSection(WorkerProxyClientOptions.SectionName));

builder.Services.PostConfigure<RouteRegistryClientOptions>(options =>
{
    options.ServiceToken ??= builder.Configuration["INTERNAL_API_SERVICE_AUTH_CLIENT_TOKEN"];
});

builder.Services.PostConfigure<IamClientOptions>(options =>
{
    options.ServiceToken ??= builder.Configuration["INTERNAL_API_SERVICE_AUTH_CLIENT_TOKEN"];
});

builder.Services.PostConfigure<EntitlementClientOptions>(options =>
{
    options.ServiceToken ??= builder.Configuration["INTERNAL_API_SERVICE_AUTH_CLIENT_TOKEN"];
});

builder.Services.PostConfigure<WorkerProxyClientOptions>(options =>
{
    options.ServiceToken ??= builder.Configuration["WORKER_API_SERVICE_AUTH_CLIENT_TOKEN"];
});

builder.Services.AddHttpClient<IRouteRegistryClient, RouteRegistryHttpClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<RouteRegistryClientOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});

builder.Services.AddHttpClient<IIamClient, IamHttpClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<IamClientOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});

builder.Services.AddHttpClient<IEntitlementClient, EntitlementHttpClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<EntitlementClientOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});

builder.Services.AddHttpClient<IWorkerProxyClient, WorkerProxyHttpClient>();

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

var allowTestExtActions = builder.Configuration.GetValue("TEST_WORKER_EXT_ACTIONS_ENABLED", false);
var app = builder.Build();

app.UseDdcrmCommonPipeline();
app.UseCors("external-cors");
app.UseAuthentication();
app.UseAuthorization();

app.MapMethods("/v1/{*path}", ["OPTIONS"], () => Results.Ok());

var gateway = app.MapGroup("/v1").RequireAuthorization();

gateway.MapPost("/account-api/{routeKey}/{action}", async (
    HttpContext httpContext,
    string routeKey,
    string action,
    Dictionary<string, JsonElement>? request,
    IRouteRegistryClient routeRegistryClient,
    IIamClient iamClient,
    IEntitlementClient entitlementClient,
    IWorkerProxyClient workerProxyClient,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    var userId = GetCurrentUserId(httpContext);

    if (string.IsNullOrWhiteSpace(routeKey))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Параметр routeKey обязателен.");
    }

    if (string.IsNullOrWhiteSpace(action) || !Regex.IsMatch(action, "^[a-z0-9._-]+$"))
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "Параметр action должен соответствовать паттерну ^[a-z0-9._-]+$.");
    }

    if (action.StartsWith("ext.test.", StringComparison.Ordinal) && !allowTestExtActions)
    {
        throw new ApiErrorException(
            StatusCodes.Status403Forbidden,
            ApiErrorCodes.Forbidden,
            "ext.test.* запрещен вне non-production профиля тестового воркера.");
    }

    var resolvedRoute = await routeRegistryClient.ResolveAsync(routeKey.Trim(), cancellationToken);
    if (resolvedRoute is null)
    {
        throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Route не найден.");
    }

    var requiredPermission = ResolvePermissionForAction(action);
    var permissionAllowed = await iamClient.CheckPermissionAsync(
        resolvedRoute.ProjectId,
        userId,
        requiredPermission,
        cancellationToken);

    if (!permissionAllowed)
    {
        throw new ApiErrorException(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "Недостаточно прав для вызова account API.");
    }

    var entitlementAllowed = await entitlementClient.IsAllowedAsync(
        resolvedRoute.ProjectId,
        userId,
        action,
        cancellationToken);

    if (!entitlementAllowed)
    {
        throw new ApiErrorException(StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden, "Операция заблокирована политикой entitlement.");
    }

    if (action.StartsWith("ext.", StringComparison.Ordinal))
    {
        var isSupported = await workerProxyClient.SupportsActionAsync(resolvedRoute, action, cancellationToken);
        if (!isSupported)
        {
            throw new ApiErrorException(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.Conflict,
                "Action не объявлен capability-набором worker-а.");
        }
    }

    var workerPayload = await workerProxyClient.InvokeAsync(
        resolvedRoute,
        action,
        request,
        idempotencyKey,
        cancellationToken);

    return Results.Ok(new ProxyResponse(httpContext.GetOrCreateRequestId(), NormalizeProxyResult(workerPayload)));
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

static JsonElement NormalizeProxyResult(JsonElement rawPayload)
{
    if (rawPayload.ValueKind == JsonValueKind.Object)
    {
        if (rawPayload.TryGetProperty("result", out var resultProperty) && resultProperty.ValueKind == JsonValueKind.Object)
        {
            return resultProperty.Clone();
        }

        if (rawPayload.TryGetProperty("data", out var dataProperty) && dataProperty.ValueKind == JsonValueKind.Object)
        {
            return dataProperty.Clone();
        }

        return rawPayload.Clone();
    }

    return JsonSerializer.SerializeToElement(new Dictionary<string, object?>
    {
        ["value"] = rawPayload.Clone(),
    });
}

static string ResolvePermissionForAction(string action)
{
    if (action.StartsWith("ext.account.lifecycle.", StringComparison.Ordinal))
    {
        return ProjectPermissions.ProjectAccountsLifecycleManage;
    }

    if (action.StartsWith("ext.account.proxy-credentials.", StringComparison.Ordinal))
    {
        return string.Equals(action, "ext.account.proxy-credentials.reveal", StringComparison.Ordinal)
            ? ProjectPermissions.ProjectAccountsProxyCredentialsReveal
            : ProjectPermissions.ProjectAccountsProxyCredentialsUpdate;
    }

    return ProjectPermissions.ProjectWorkersOperate;
}

public sealed record ProxyResponse(string RequestId, JsonElement Result);

public partial class Program;
