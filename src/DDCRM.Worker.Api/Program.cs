using System.Text.Json;
using System.Text.RegularExpressions;
using DDCRM.Worker.Api;
using DDCRM.Shared.Auth;
using DDCRM.Shared.Errors;
using DDCRM.Shared.Extensions;
using DDCRM.Shared.Idempotency;
using DDCRM.Worker.Api.Simulation;
using DDCRM.Worker.Persistence;
using DDCRM.Worker.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<WorkerDbContext>((serviceProvider, options) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var useInMemoryDb = configuration.GetValue("TEST_USE_INMEMORY_DB", false);

    if (useInMemoryDb)
    {
        options.UseInMemoryDatabase(configuration["TEST_INMEMORY_DB_NAME"] ?? "ddcrm-worker-tests");
        return;
    }

    options.UseNpgsql(
        configuration.GetConnectionString("WorkerDb")
        ?? configuration["WORKER_DB_CONNECTION"]
        ?? "Host=localhost;Port=5432;Database=ddcrm_worker;Username=postgres;Password=postgres");
});

builder.Services.AddScoped<IdempotencyExecutor>();

builder.Services.AddServiceTokenAuth(options =>
{
    options.Enabled = builder.Configuration.GetValue("WORKER_API_SERVICE_AUTH_ENABLED", true);
    options.AcceptedTokens = builder.Configuration.GetCommaSeparatedValues("WORKER_API_SERVICE_AUTH_ACCEPTED_TOKENS");
    options.ForbiddenTokens = builder.Configuration.GetCommaSeparatedValues("INTERNAL_API_SERVICE_AUTH_ACCEPTED_TOKENS");
    options.MissingTokenErrorCode = WorkerErrorCodes.AuthFailed;
    options.InvalidTokenErrorCode = WorkerErrorCodes.AuthFailed;
    options.ForbiddenTokenErrorCode = WorkerErrorCodes.AuthFailed;
    options.InvalidTokenErrorMessage = "Невалидный worker service-auth токен.";
    options.ForbiddenTokenErrorMessage = "Токен internal-контура не может использоваться в worker API.";
});

builder.Services.Configure<TestWorkerOptions>(options =>
{
    options.Enabled = builder.Configuration.GetValue("TEST_WORKER_ENABLED", false);
    options.DefaultVisibility = builder.Configuration["TEST_WORKER_DEFAULT_VISIBILITY"] ?? "hidden";
    options.AllowedEnvironments = builder.Configuration.GetCommaSeparatedValues("TEST_WORKER_ALLOWED_ENVIRONMENTS");
    options.BlockInProduction = builder.Configuration.GetValue("TEST_WORKER_BLOCK_IN_PRODUCTION", true);
    options.Scenario = builder.Configuration["TEST_WORKER_SCENARIO"] ?? WorkerScenarioIds.HappyPath;
    options.CapabilityProfile = builder.Configuration["TEST_WORKER_CAPABILITY_PROFILE"] ?? WorkerCapabilityProfiles.CoreV1;
    options.FixtureSource = builder.Configuration["TEST_WORKER_FIXTURE_SOURCE"] ?? "./fixtures/test-worker";
    options.FixtureRevision = builder.Configuration["TEST_WORKER_FIXTURE_REVISION"] ?? "main";
    options.ExtActionsEnabled = builder.Configuration.GetValue("TEST_WORKER_EXT_ACTIONS_ENABLED", true);
});

builder.Services.AddSingleton<TransientFailureState>();

var app = builder.Build();
var runtimeSettings = ResolveRuntimeSettings(
    app.Environment,
    app.Services.GetRequiredService<IOptions<TestWorkerOptions>>().Value);

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
    if (dbContext.Database.IsRelational())
    {
        dbContext.Database.Migrate();
    }
    else
    {
        dbContext.Database.EnsureCreated();
    }

    SeedWorkerFixtures(dbContext);
}

var startedAtUtc = DateTimeOffset.UtcNow;

app.UseDdcrmCommonPipeline();
app.UseServiceTokenAuth();

var worker = app.MapGroup("/internal/v1/worker");

worker.MapGet("/health", (HttpContext httpContext) =>
{
    var uptime = Math.Max(0, (long)(DateTimeOffset.UtcNow - startedAtUtc).TotalSeconds);
    return Results.Ok(new HealthResponse(httpContext.GetOrCreateRequestId(), "ok", uptime));
});

worker.MapGet("/capabilities", (HttpContext httpContext) =>
{
    var requestId = httpContext.GetOrCreateRequestId();

    if (runtimeSettings.Scenario == WorkerScenarioIds.ContractDrift)
    {
        // Намеренный drift-ответ для `TW-SCN-CONTRACT-DRIFT` (non-production симуляция).
        return Results.Json(new Dictionary<string, object?>
        {
            ["requestId"] = requestId,
            ["capabilities"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["key"] = "ext.test.contract-drift",
                }
            },
        });
    }

    var capabilities = runtimeSettings.Capabilities
        .Select(x => new CapabilityItem(x, true))
        .ToArray();

    return Results.Ok(new CapabilitiesResponse(requestId, capabilities));
});

worker.MapGet("/account", (HttpContext httpContext, TransientFailureState transientFailureState) =>
{
    ApplyScenarioGuard(runtimeSettings, transientFailureState, "workerAccountInfo");

    var account = new AccountInfo(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "test-worker",
        "DDCRM Test Worker Account",
        "active",
        1000.00m);

    return Results.Ok(new AccountInfoResponse(httpContext.GetOrCreateRequestId(), account));
});

worker.MapPost("/listings/search", async (
    HttpContext httpContext,
    ListingsSearchRequest? request,
    WorkerDbContext dbContext,
    TransientFailureState transientFailureState,
    CancellationToken cancellationToken) =>
{
    ApplyScenarioGuard(runtimeSettings, transientFailureState, "workerListingsSearch");

    var requestId = httpContext.GetOrCreateRequestId();
    if (runtimeSettings.Scenario == WorkerScenarioIds.MalformedPayload)
    {
        // Намеренный malformed payload для `TW-SCN-MALFORMED-PAYLOAD`.
        return Results.Json(new Dictionary<string, object?>
        {
            ["requestId"] = requestId,
            ["items"] = "malformed-items",
        });
    }

    var limit = NormalizeLimit(request?.Limit);
    var statuses = request?.Statuses is { Count: > 0 }
        ? request.Statuses.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase)
        : null;
    var query = string.IsNullOrWhiteSpace(request?.Query) ? null : request.Query.Trim();

    var listings = await dbContext.Listings
        .AsNoTracking()
        .OrderBy(x => x.Id)
        .ToListAsync(cancellationToken);

    IEnumerable<WorkerListingEntity> filtered = listings;

    if (statuses is not null)
    {
        filtered = filtered.Where(x => statuses.Contains(x.Status));
    }

    if (!string.IsNullOrWhiteSpace(query))
    {
        filtered = filtered.Where(x => x.Title.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    var filteredList = filtered.ToList();
    var (paged, nextCursor) = ApplyCursorPaging(filteredList, limit, request?.Cursor, x => x.Id);
    var items = paged.Select(ToListing).ToArray();

    return Results.Ok(new ListingsSearchResponse(requestId, items, new PagingMeta(nextCursor)));
});

worker.MapPatch("/listings/{listingId}", async (
    HttpContext httpContext,
    string listingId,
    ListingUpdateRequest request,
    WorkerDbContext dbContext,
    IdempotencyExecutor idempotency,
    TransientFailureState transientFailureState,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(listingId))
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "listingId обязателен.");
    }

    if (request.Title is null && request.Price is null && request.Status is null)
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "Требуется минимум одно поле для обновления listing.");
    }

    ApplyScenarioGuard(runtimeSettings, transientFailureState, "workerListingUpdate");

    var idempotencyKey = httpContext.RequireIdempotencyKey();
    return await idempotency.ExecuteAsync(
        dbContext,
        $"worker:listing:update:{listingId.Trim()}",
        idempotencyKey,
        async ct =>
        {
            var listing = await dbContext.Listings.SingleOrDefaultAsync(x => x.Id == listingId.Trim(), ct);
            if (listing is null)
            {
                throw CreateRuntimeConflict("Объявление не найдено в текущем runtime state.");
            }

            if (request.Title is not null)
            {
                if (string.IsNullOrWhiteSpace(request.Title))
                {
                    throw CreatePlatformError(StatusCodes.Status400BadRequest, "title не может быть пустым.");
                }

                listing.Title = request.Title.Trim();
            }

            if (request.Status is not null)
            {
                if (string.IsNullOrWhiteSpace(request.Status))
                {
                    throw CreatePlatformError(StatusCodes.Status400BadRequest, "status не может быть пустым.");
                }

                listing.Status = request.Status.Trim();
            }

            if (request.Price is not null)
            {
                if (request.Price < 0)
                {
                    throw CreatePlatformError(StatusCodes.Status400BadRequest, "price не может быть отрицательным.");
                }

                listing.Price = decimal.Round(request.Price.Value, 2);
            }

            listing.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new ListingResponse(httpContext.GetOrCreateRequestId(), ToListing(listing)));
        },
        cancellationToken);
});

worker.MapPost("/messages/send", async (
    HttpContext httpContext,
    MessageSendRequest request,
    WorkerDbContext dbContext,
    IdempotencyExecutor idempotency,
    TransientFailureState transientFailureState,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.ThreadId))
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "threadId обязателен.");
    }

    if (string.IsNullOrWhiteSpace(request.Text))
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "text обязателен.");
    }

    ApplyScenarioGuard(runtimeSettings, transientFailureState, "workerMessageSend");

    var idempotencyKey = httpContext.RequireIdempotencyKey();

    return await idempotency.ExecuteAsync(
        dbContext,
        $"worker:messages:send:{request.ThreadId.Trim()}",
        idempotencyKey,
        _ =>
        {
            var messageId = $"msg-{Guid.NewGuid():N}"[..20];
            return Task.FromResult(new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new MessageSendResponse(httpContext.GetOrCreateRequestId(), messageId)));
        },
        cancellationToken);
});

worker.MapPost("/orders/search", async (
    HttpContext httpContext,
    OrdersSearchRequest? request,
    WorkerDbContext dbContext,
    TransientFailureState transientFailureState,
    CancellationToken cancellationToken) =>
{
    ApplyScenarioGuard(runtimeSettings, transientFailureState, "workerOrdersSearch");

    var limit = NormalizeLimit(request?.Limit);
    var statuses = request?.Statuses is { Count: > 0 }
        ? request.Statuses.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase)
        : null;

    var orders = await dbContext.Orders
        .AsNoTracking()
        .OrderBy(x => x.Id)
        .ToListAsync(cancellationToken);

    IEnumerable<WorkerOrderEntity> filtered = orders;

    if (statuses is not null)
    {
        filtered = filtered.Where(x => statuses.Contains(x.Status));
    }

    if (request?.FromDate is not null)
    {
        filtered = filtered.Where(x => x.UpdatedAtUtc >= request.FromDate.Value);
    }

    if (request?.ToDate is not null)
    {
        filtered = filtered.Where(x => x.UpdatedAtUtc <= request.ToDate.Value);
    }

    var filteredList = filtered.ToList();
    var (paged, nextCursor) = ApplyCursorPaging(filteredList, limit, request?.Cursor, x => x.Id);
    var items = paged.Select(ToOrder).ToArray();

    return Results.Ok(new OrdersSearchResponse(httpContext.GetOrCreateRequestId(), items, new PagingMeta(nextCursor)));
});

worker.MapPost("/orders/{orderId}/actions/{action}", async (
    HttpContext httpContext,
    string orderId,
    string action,
    OrderActionRequest? request,
    WorkerDbContext dbContext,
    IdempotencyExecutor idempotency,
    TransientFailureState transientFailureState,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(orderId))
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "orderId обязателен.");
    }

    ValidateAction(action);
    if (!action.StartsWith("orders.", StringComparison.Ordinal))
    {
        throw CreateInvalidActionError("Для endpoint orders/actions поддерживаются только action с префиксом orders.");
    }

    ApplyScenarioGuard(runtimeSettings, transientFailureState, $"workerOrderAction:{action}");

    var idempotencyKey = httpContext.RequireIdempotencyKey();
    return await idempotency.ExecuteAsync(
        dbContext,
        $"worker:orders:action:{orderId.Trim()}:{action}",
        idempotencyKey,
        async ct =>
        {
            var order = await dbContext.Orders.SingleOrDefaultAsync(x => x.Id == orderId.Trim(), ct);
            if (order is null)
            {
                throw CreateRuntimeConflict("Заказ не найден в текущем runtime state.");
            }

            order.Status = action switch
            {
                "orders.cancel" => "cancelled",
                "orders.ship" => "shipped",
                "orders.complete" => "completed",
                _ => throw CreateInvalidActionError($"Неподдерживаемый action для заказа: {action}."),
            };

            order.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(ct);

            var result = new Dictionary<string, object?>
            {
                ["orderId"] = order.Id,
                ["action"] = action,
                ["status"] = order.Status,
                ["payload"] = request?.Payload,
            };

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new OrderActionResponse(httpContext.GetOrCreateRequestId(), result));
        },
        cancellationToken);
});

worker.MapPost("/actions/{action}", async (
    HttpContext httpContext,
    string action,
    ExtensionActionRequest? request,
    WorkerDbContext dbContext,
    IdempotencyExecutor idempotency,
    TransientFailureState transientFailureState,
    CancellationToken cancellationToken) =>
{
    ValidateAction(action);
    if (!action.StartsWith("ext.", StringComparison.Ordinal))
    {
        throw CreateInvalidActionError("Extension endpoint принимает только ext.* action.");
    }

    if (action.StartsWith("ext.test.", StringComparison.Ordinal) && !runtimeSettings.AllowTestExtensions)
    {
        throw new ApiErrorException(
            StatusCodes.Status403Forbidden,
            WorkerErrorCodes.InvalidAction,
            "ext.test.* запрещён вне non-production профиля тестового воркера.");
    }

    ApplyScenarioGuard(runtimeSettings, transientFailureState, $"workerExtensionAction:{action}");

    var isSupported = runtimeSettings.Capabilities.Contains(action);
    if (!isSupported || runtimeSettings.Scenario == WorkerScenarioIds.CapabilityMismatch)
    {
        throw CreateRuntimeConflict("Action не объявлен capability-набором worker-а.");
    }

    var idempotencyKey = httpContext.RequireIdempotencyKey();
    return await idempotency.ExecuteAsync(
        dbContext,
        $"worker:extension:{action}",
        idempotencyKey,
        _ =>
        {
            var result = new Dictionary<string, object?>
            {
                ["action"] = action,
                ["accepted"] = true,
                ["scenario"] = runtimeSettings.Scenario,
                ["fixtureSource"] = runtimeSettings.FixtureSource,
                ["fixtureRevision"] = runtimeSettings.FixtureRevision,
                ["payload"] = request?.Payload,
            };

            return Task.FromResult(new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new ExtensionActionResponse(httpContext.GetOrCreateRequestId(), result, [])));
        },
        cancellationToken);
});

app.Run();

static WorkerRuntimeSettings ResolveRuntimeSettings(IHostEnvironment hostEnvironment, TestWorkerOptions options)
{
    var scenario = string.IsNullOrWhiteSpace(options.Scenario) ? WorkerScenarioIds.HappyPath : options.Scenario.Trim();
    if (!WorkerSimulationCatalog.IsKnownScenario(scenario))
    {
        throw new InvalidOperationException($"Неизвестный TEST_WORKER_SCENARIO: {scenario}.");
    }

    var capabilityProfile = string.IsNullOrWhiteSpace(options.CapabilityProfile)
        ? WorkerCapabilityProfiles.CoreV1
        : options.CapabilityProfile.Trim();
    if (!WorkerSimulationCatalog.IsKnownProfile(capabilityProfile))
    {
        throw new InvalidOperationException($"Неизвестный TEST_WORKER_CAPABILITY_PROFILE: {capabilityProfile}.");
    }

    var allowedEnvironments = options.AllowedEnvironments.Length == 0
        ? ["local", "ci", "staging"]
        : options.AllowedEnvironments
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .ToArray();

    var currentEnvironment = hostEnvironment.EnvironmentName.ToLowerInvariant();
    if (hostEnvironment.IsProduction() && options.Enabled && options.BlockInProduction)
    {
        throw new InvalidOperationException("TEST_WORKER_BLOCK_IN_PRODUCTION=true блокирует запуск тестового воркера в production.");
    }

    if (options.Enabled && !allowedEnvironments.Contains(currentEnvironment, StringComparer.Ordinal))
    {
        throw new InvalidOperationException(
            $"Контур {hostEnvironment.EnvironmentName} не входит в TEST_WORKER_ALLOWED_ENVIRONMENTS ({string.Join(",", allowedEnvironments)}).");
    }

    var allowTestExtensions = options.Enabled && options.ExtActionsEnabled && !hostEnvironment.IsProduction();
    var capabilities = WorkerSimulationCatalog.GetCapabilities(capabilityProfile, allowTestExtensions)
        .ToHashSet(StringComparer.Ordinal);

    return new WorkerRuntimeSettings(
        options.Enabled,
        scenario,
        capabilityProfile,
        options.FixtureSource,
        options.FixtureRevision,
        options.ExtActionsEnabled,
        allowTestExtensions,
        capabilities);
}

static void SeedWorkerFixtures(WorkerDbContext dbContext)
{
    if (!dbContext.Listings.Any())
    {
        dbContext.Listings.AddRange(
            new WorkerListingEntity
            {
                Id = "listing-100",
                Title = "Gaming Laptop RTX",
                Status = "active",
                Price = 1499.99m,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            },
            new WorkerListingEntity
            {
                Id = "listing-200",
                Title = "Wireless Keyboard",
                Status = "paused",
                Price = 89.50m,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
    }

    if (!dbContext.Orders.Any())
    {
        dbContext.Orders.AddRange(
            new WorkerOrderEntity
            {
                Id = "order-100",
                Status = "new",
                Total = 1499.99m,
                Currency = "USD",
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            },
            new WorkerOrderEntity
            {
                Id = "order-200",
                Status = "processing",
                Total = 89.50m,
                Currency = "USD",
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
    }

    dbContext.SaveChanges();
}

static void ApplyScenarioGuard(
    WorkerRuntimeSettings runtimeSettings,
    TransientFailureState transientFailureState,
    string operationKey)
{
    switch (runtimeSettings.Scenario)
    {
        case WorkerScenarioIds.AuthFail:
            throw new ApiErrorException(
                StatusCodes.Status401Unauthorized,
                WorkerErrorCodes.AuthFailed,
                "Сценарий тестового воркера вернул ошибку авторизации площадки.");

        case WorkerScenarioIds.Timeout:
            throw new ApiErrorException(
                StatusCodes.Status503ServiceUnavailable,
                WorkerErrorCodes.Unavailable,
                "Сценарий тестового воркера вернул timeout внешней площадки.");

        case WorkerScenarioIds.RateLimit:
            throw new ApiErrorException(
                StatusCodes.Status429TooManyRequests,
                WorkerErrorCodes.PlatformError,
                "Сценарий тестового воркера вернул rate-limit внешней площадки.");

        case WorkerScenarioIds.TransientError when transientFailureState.ShouldFail(operationKey):
            throw new ApiErrorException(
                StatusCodes.Status503ServiceUnavailable,
                WorkerErrorCodes.Unavailable,
                "Сценарий тестового воркера вернул временную ошибку площадки.");
    }
}

static void ValidateAction(string action)
{
    if (string.IsNullOrWhiteSpace(action) || !Regex.IsMatch(action, "^[a-z0-9._-]+$"))
    {
        throw CreateInvalidActionError("action должен соответствовать паттерну ^[a-z0-9._-]+$.");
    }
}

static ApiErrorException CreateInvalidActionError(string message)
{
    return new ApiErrorException(StatusCodes.Status400BadRequest, WorkerErrorCodes.InvalidAction, message);
}

static ApiErrorException CreateRuntimeConflict(string message)
{
    return new ApiErrorException(StatusCodes.Status409Conflict, WorkerErrorCodes.RuntimeConflict, message);
}

static ApiErrorException CreatePlatformError(int statusCode, string message)
{
    return new ApiErrorException(statusCode, WorkerErrorCodes.PlatformError, message);
}

static int NormalizeLimit(int? limit)
{
    if (limit is null)
    {
        return 50;
    }

    if (limit < 1 || limit > 200)
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "limit должен быть в диапазоне 1..200.");
    }

    return limit.Value;
}

static (IReadOnlyCollection<T> Items, string? NextCursor) ApplyCursorPaging<T>(
    IReadOnlyCollection<T> source,
    int limit,
    string? cursor,
    Func<T, string> cursorSelector)
{
    var filtered = string.IsNullOrWhiteSpace(cursor)
        ? source.ToList()
        : source.Where(x => string.CompareOrdinal(cursorSelector(x), cursor.Trim()) > 0).ToList();

    var items = filtered.Take(limit).ToList();
    var nextCursor = filtered.Count > limit && items.Count > 0
        ? cursorSelector(items[^1])
        : null;

    return (items, nextCursor);
}

static Listing ToListing(WorkerListingEntity entity) => new(entity.Id, entity.Title, entity.Status, entity.Price);

static Order ToOrder(WorkerOrderEntity entity) => new(entity.Id, entity.Status, entity.Total, entity.Currency);

public sealed record WorkerRuntimeSettings(
    bool TestWorkerEnabled,
    string Scenario,
    string CapabilityProfile,
    string FixtureSource,
    string FixtureRevision,
    bool ExtActionsEnabled,
    bool AllowTestExtensions,
    IReadOnlySet<string> Capabilities);

public sealed record HealthResponse(string RequestId, string Status, long UptimeSeconds);

public sealed record CapabilityItem(string Key, bool Enabled);

public sealed record CapabilitiesResponse(string RequestId, IReadOnlyCollection<CapabilityItem> Capabilities);

public sealed record AccountInfo(
    Guid AccountId,
    Guid ProjectId,
    string Platform,
    string DisplayName,
    string Status,
    decimal Balance);

public sealed record AccountInfoResponse(string RequestId, AccountInfo Account);

public sealed record ListingsSearchRequest(int? Limit, string? Cursor, string? Query, IReadOnlyCollection<string>? Statuses);

public sealed record Listing(string Id, string Title, string Status, decimal Price);

public sealed record PagingMeta(string? NextCursor);

public sealed record ListingsSearchResponse(string RequestId, IReadOnlyCollection<Listing> Items, PagingMeta Paging);

public sealed record ListingUpdateRequest(string? Title, decimal? Price, string? Status);

public sealed record ListingResponse(string RequestId, Listing Listing);

public sealed record MessageSendRequest(string ThreadId, string Text, IReadOnlyCollection<string>? Attachments);

public sealed record MessageSendResponse(string RequestId, string MessageId);

public sealed record OrdersSearchRequest(
    int? Limit,
    string? Cursor,
    IReadOnlyCollection<string>? Statuses,
    DateTimeOffset? FromDate,
    DateTimeOffset? ToDate);

public sealed record Order(string Id, string Status, decimal Total, string Currency);

public sealed record OrdersSearchResponse(string RequestId, IReadOnlyCollection<Order> Items, PagingMeta Paging);

public sealed record OrderActionRequest(Dictionary<string, JsonElement>? Payload);

public sealed record OrderActionResponse(string RequestId, Dictionary<string, object?> Result);

public sealed record ExtensionActionRequest(Dictionary<string, JsonElement>? Payload);

public sealed record ExtensionActionResponse(
    string RequestId,
    Dictionary<string, object?> Result,
    IReadOnlyCollection<string>? Warnings);

public partial class Program;
