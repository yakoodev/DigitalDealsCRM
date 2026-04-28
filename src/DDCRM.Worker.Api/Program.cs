using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DDCRM.Worker.Api;
using DDCRM.Shared.Auth;
using DDCRM.Shared.Errors;
using DDCRM.Shared.Extensions;
using DDCRM.Shared.Idempotency;
using DDCRM.Worker.Api.Simulation;
using DDCRM.Worker.Persistence;
using DDCRM.Worker.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;
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
    options.Provider = builder.Configuration["TEST_WORKER_PROVIDER"] ?? WorkerV2ProviderAliases.PlatiMarket;
});

builder.Services.AddSingleton<TransientFailureState>();
var proxyCredentialsEncryptionKey = ResolveProxyCredentialsEncryptionKey(
    builder.Configuration["WORKER_PROXY_CREDENTIALS_ENCRYPTION_KEY"]);
var marketplaceAuthEncryptionKey = ResolveProxyCredentialsEncryptionKey(
    builder.Configuration["WORKER_MARKETPLACE_AUTH_ENCRYPTION_KEY"]);

var app = builder.Build();
var runtimeSettings = ResolveRuntimeSettings(
    app.Environment,
    app.Services.GetRequiredService<IOptions<TestWorkerOptions>>().Value);
var workerInstanceId = ResolveWorkerInstanceId(builder.Configuration);

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
var v2State = WorkerV2State.CreateDefault();
var workerV2Features = ResolveWorkerV2FeatureMap(runtimeSettings.Provider);

app.UseDdcrmCommonPipeline();
app.UseServiceTokenAuth();

app.MapGet("/health", (HttpContext httpContext) =>
    Results.Ok(new
    {
        requestId = httpContext.GetOrCreateRequestId(),
        status = "ok",
    }));

var worker = app.MapGroup("/internal/v2/worker");

worker.MapGet("/health", (HttpContext httpContext) =>
{
    var uptime = Math.Max(0, (long)(DateTimeOffset.UtcNow - startedAtUtc).TotalSeconds);
    return Results.Ok(new HealthResponse(httpContext.GetOrCreateRequestId(), "ok", uptime));
});

worker.MapGet("/capabilities", (HttpContext httpContext) =>
{
    var requestId = httpContext.GetOrCreateRequestId();
    var provider = runtimeSettings.Provider;
    var capabilityItems = runtimeSettings.Capabilities
        .Select(x => new CapabilityItem(x, true))
        .ToArray();

    if (runtimeSettings.Scenario == WorkerScenarioIds.ContractDrift)
    {
        // Намеренный drift-ответ для `TW-SCN-CONTRACT-DRIFT` (non-production симуляция).
        return Results.Json(new Dictionary<string, object?>
        {
            ["requestId"] = requestId,
            ["provider"] = "contract-drift",
            ["capabilities"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["key"] = "ext.test.contract-drift",
                }
            },
        });
    }

    return Results.Ok(new WorkerV2CapabilitiesResponse(requestId, provider, workerV2Features, capabilityItems));
});

worker.MapGet("/account", (HttpContext httpContext, TransientFailureState transientFailureState) =>
{
    EnsureWorkerV2FeatureEnabled(workerV2Features, "account.info");
    ApplyScenarioGuard(runtimeSettings, transientFailureState, "workerV2AccountInfo");
    var descriptor = ResolveWorkerV2AccountDescriptor(runtimeSettings.Provider);

    var profile = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["displayName"] = descriptor.DisplayName,
        ["balance"] = descriptor.Balance,
        ["sandbox"] = true,
    };

    var raw = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["projectId"] = "22222222-2222-2222-2222-222222222222",
        ["workerMode"] = runtimeSettings.TestWorkerEnabled ? "test-worker" : "runtime",
        ["workerInstanceId"] = workerInstanceId,
        ["workerMachineName"] = Environment.MachineName,
        ["workerStartedAtUtc"] = startedAtUtc,
    };

    var account = new WorkerV2AccountInfo(
        runtimeSettings.Provider,
        descriptor.AccountId,
        descriptor.Nickname,
        "active",
        profile,
        raw);

    return Results.Ok(new WorkerV2AccountInfoResponse(httpContext.GetOrCreateRequestId(), account));
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

    if (string.Equals(action, WorkerExtensionActionKeys.ProxyCredentialsApply, StringComparison.Ordinal))
    {
        var (accountId, proxyConfig) = ReadProxyCredentialsApplyPayload(request);
        var requestId = httpContext.GetOrCreateRequestId();
        var idempotencyKey = httpContext.RequireIdempotencyKey();

        return await idempotency.ExecuteAsync(
            dbContext,
            $"worker:proxy-credentials:apply:{accountId}",
            idempotencyKey,
            async ct =>
            {
                var existing = await dbContext.ProxyCredentials.SingleOrDefaultAsync(x => x.AccountId == accountId, ct);
                if (existing is null)
                {
                    dbContext.ProxyCredentials.Add(new WorkerProxyCredentialsEntity
                    {
                        AccountId = accountId,
                        Host = proxyConfig.Host,
                        Port = proxyConfig.Port,
                        Login = proxyConfig.Login,
                        Password = EncryptProxySecret(proxyConfig.Password, proxyCredentialsEncryptionKey),
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                    });
                }
                else
                {
                    existing.Host = proxyConfig.Host;
                    existing.Port = proxyConfig.Port;
                    existing.Login = proxyConfig.Login;
                    existing.Password = EncryptProxySecret(proxyConfig.Password, proxyCredentialsEncryptionKey);
                    existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
                }

                return new IdempotentExecutionResult(
                    StatusCodes.Status200OK,
                    new ExtensionActionResponse(
                        requestId,
                        new Dictionary<string, object?>
                        {
                            ["status"] = "applied",
                            ["accountId"] = accountId,
                        },
                        []));
            },
            cancellationToken);
    }

    if (string.Equals(action, WorkerExtensionActionKeys.ProxyCredentialsReveal, StringComparison.Ordinal))
    {
        var accountId = ReadProxyCredentialsRevealAccountId(request);
        _ = httpContext.RequireIdempotencyKey();

        var credentials = await dbContext.ProxyCredentials
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.AccountId == accountId, cancellationToken);

        if (credentials is null)
        {
            throw CreateRuntimeConflict("Proxy credentials не найдены в worker state storage.");
        }

        return Results.Ok(new ExtensionActionResponse(
            httpContext.GetOrCreateRequestId(),
            new Dictionary<string, object?>
            {
                ["proxyConfig"] = new Dictionary<string, object?>
                {
                    ["host"] = credentials.Host,
                    ["port"] = credentials.Port,
                    ["login"] = credentials.Login,
                    ["password"] = DecryptProxySecret(credentials.Password, proxyCredentialsEncryptionKey),
                },
            },
            []));
    }

    if (string.Equals(action, WorkerExtensionActionKeys.MarketplaceAuthApply, StringComparison.Ordinal))
    {
        var (accountId, marketplaceAuth) = ReadMarketplaceAuthApplyPayload(request);
        var requestId = httpContext.GetOrCreateRequestId();
        var idempotencyKey = httpContext.RequireIdempotencyKey();

        return await idempotency.ExecuteAsync(
            dbContext,
            $"worker:marketplace-auth:apply:{accountId}",
            idempotencyKey,
            async ct =>
            {
                var credentialsJson = JsonSerializer.Serialize(marketplaceAuth.Credentials);
                var existing = await dbContext.MarketplaceAuth.SingleOrDefaultAsync(x => x.AccountId == accountId, ct);
                if (existing is null)
                {
                    dbContext.MarketplaceAuth.Add(new WorkerMarketplaceAuthEntity
                    {
                        AccountId = accountId,
                        Scheme = marketplaceAuth.Scheme,
                        CredentialsEncrypted = EncryptProxySecret(credentialsJson, marketplaceAuthEncryptionKey),
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                    });
                }
                else
                {
                    existing.Scheme = marketplaceAuth.Scheme;
                    existing.CredentialsEncrypted = EncryptProxySecret(credentialsJson, marketplaceAuthEncryptionKey);
                    existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
                }

                return new IdempotentExecutionResult(
                    StatusCodes.Status200OK,
                    new ExtensionActionResponse(
                        requestId,
                        new Dictionary<string, object?>
                        {
                            ["status"] = "applied",
                            ["accountId"] = accountId,
                            ["scheme"] = marketplaceAuth.Scheme,
                        },
                        []));
            },
            cancellationToken);
    }

    var defaultIdempotencyKey = httpContext.RequireIdempotencyKey();
    return await idempotency.ExecuteAsync(
        dbContext,
        $"worker:extension:{action}",
        defaultIdempotencyKey,
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

var workerV2 = worker;

workerV2.MapGet("/conversations", (
    HttpContext httpContext,
    int? limit,
    string? cursor,
    bool? onlyUnread,
    TransientFailureState transientFailureState) =>
{
    EnsureWorkerV2FeatureEnabled(workerV2Features, "conversations.list");
    ApplyScenarioGuard(runtimeSettings, transientFailureState, "workerV2ConversationsList");

    if (runtimeSettings.Scenario == WorkerScenarioIds.MalformedPayload)
    {
        // Намеренный malformed payload для `TW-SCN-MALFORMED-PAYLOAD`.
        return Results.Json(new Dictionary<string, object?>
        {
            ["requestId"] = httpContext.GetOrCreateRequestId(),
            ["items"] = "malformed-items",
        });
    }

    var normalizedLimit = NormalizeLimit(limit);
    var conversations = v2State.GetConversationSummaries(onlyUnread ?? false)
        .OrderBy(x => x.ConversationId, StringComparer.Ordinal)
        .ToList();
    var (paged, nextCursor) = ApplyCursorPaging(conversations, normalizedLimit, cursor, x => x.ConversationId);

    return Results.Ok(new WorkerV2ConversationsResponse(httpContext.GetOrCreateRequestId(), paged, nextCursor));
});

workerV2.MapGet("/conversations/{conversationId}/messages", (
    HttpContext httpContext,
    string conversationId,
    int? limit,
    string? cursor,
    TransientFailureState transientFailureState) =>
{
    EnsureWorkerV2FeatureEnabled(workerV2Features, "conversations.messages.list");

    if (string.IsNullOrWhiteSpace(conversationId))
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "conversationId обязателен.");
    }

    ApplyScenarioGuard(runtimeSettings, transientFailureState, "workerV2ConversationMessages");

    var normalizedLimit = NormalizeLimit(limit);
    var messages = v2State.GetConversationMessages(conversationId.Trim())
        .OrderBy(x => x.MessageId, StringComparer.Ordinal)
        .ToList();
    var (paged, nextCursor) = ApplyCursorPaging(messages, normalizedLimit, cursor, x => x.MessageId);

    return Results.Ok(new WorkerV2ConversationMessagesResponse(httpContext.GetOrCreateRequestId(), paged, nextCursor));
});

workerV2.MapPost("/conversations/{conversationId}/messages", async (
    HttpContext httpContext,
    string conversationId,
    WorkerV2MessageSendRequest request,
    WorkerDbContext dbContext,
    IdempotencyExecutor idempotency,
    TransientFailureState transientFailureState,
    CancellationToken cancellationToken) =>
{
    EnsureWorkerV2FeatureEnabled(workerV2Features, "conversations.messages.send");

    if (string.IsNullOrWhiteSpace(conversationId))
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "conversationId обязателен.");
    }

    if (string.IsNullOrWhiteSpace(request.Text))
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "text обязателен.");
    }

    ApplyScenarioGuard(runtimeSettings, transientFailureState, "workerV2ConversationMessageSend");

    var idempotencyKey = httpContext.RequireIdempotencyKey();
    return await idempotency.ExecuteAsync(
        dbContext,
        $"worker:v2:conversations:{conversationId.Trim()}:messages:send",
        idempotencyKey,
        _ =>
        {
            var message = v2State.AddOutgoingMessage(
                conversationId.Trim(),
                request.Text.Trim(),
                request.Attachments ?? []);

            return Task.FromResult(new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new WorkerV2MessageSendResponse(
                    httpContext.GetOrCreateRequestId(),
                    message.MessageId,
                    "sent",
                    message.CreatedAt)));
        },
        cancellationToken);
});

workerV2.MapGet("/products", async (
    HttpContext httpContext,
    int? limit,
    string? cursor,
    string? status,
    WorkerDbContext dbContext,
    TransientFailureState transientFailureState,
    CancellationToken cancellationToken) =>
{
    EnsureWorkerV2FeatureEnabled(workerV2Features, "products.list");
    ApplyScenarioGuard(runtimeSettings, transientFailureState, "workerV2ProductsList");

    var normalizedLimit = NormalizeLimit(limit);
    var statusFilter = string.IsNullOrWhiteSpace(status) ? null : status.Trim();

    var listings = await dbContext.Listings
        .AsNoTracking()
        .OrderBy(x => x.Id)
        .ToListAsync(cancellationToken);

    IEnumerable<WorkerListingEntity> filtered = listings;
    if (statusFilter is not null)
    {
        filtered = filtered.Where(x => string.Equals(x.Status, statusFilter, StringComparison.OrdinalIgnoreCase));
    }

    var filteredList = filtered.ToList();
    var (paged, nextCursor) = ApplyCursorPaging(filteredList, normalizedLimit, cursor, x => x.Id);
    var items = paged
        .Select(x => ToWorkerV2Product(x, v2State.GetOrCreateProductState(x.Id)))
        .ToArray();

    return Results.Ok(new WorkerV2ProductsResponse(httpContext.GetOrCreateRequestId(), items, nextCursor));
});

workerV2.MapPost("/products", async (
    HttpContext httpContext,
    WorkerV2ProductCreateRequest request,
    WorkerDbContext dbContext,
    IdempotencyExecutor idempotency,
    TransientFailureState transientFailureState,
    CancellationToken cancellationToken) =>
{
    EnsureWorkerV2FeatureEnabled(workerV2Features, "products.create");

    if (string.IsNullOrWhiteSpace(request.SchemaId))
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "schemaId обязателен.");
    }

    if (string.IsNullOrWhiteSpace(request.Title))
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "title обязателен.");
    }

    if (request.Price.Amount < 0)
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "price.amount не может быть отрицательным.");
    }

    var normalizedCreateCurrency = NormalizeCurrency(request.Price.Currency);

    if (request.Quantity is < 0)
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "quantity не может быть отрицательным.");
    }

    if (request.Status is not null && string.IsNullOrWhiteSpace(request.Status))
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "status не может быть пустым.");
    }

    ValidateWorkerV2ProductCreateSchema(runtimeSettings.Provider, request);

    ApplyScenarioGuard(runtimeSettings, transientFailureState, "workerV2ProductCreate");

    var idempotencyKey = httpContext.RequireIdempotencyKey();
    return await idempotency.ExecuteAsync(
        dbContext,
        "worker:v2:products:create",
        idempotencyKey,
        async ct =>
        {
            var now = DateTimeOffset.UtcNow;
            var productId = $"product-{Guid.NewGuid():N}";
            var listing = new WorkerListingEntity
            {
                Id = productId,
                Title = request.Title.Trim(),
                Status = NormalizeProductStatus(request.Status, "draft"),
                Price = decimal.Round(request.Price.Amount, 2),
                UpdatedAtUtc = now,
            };

            dbContext.Listings.Add(listing);
            await dbContext.SaveChangesAsync(ct);

            var createdState = WorkerV2ProductState.FromRequest(request);
            createdState.Currency = normalizedCreateCurrency;
            v2State.SetProductState(productId, createdState);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new WorkerV2ProductMutationResponse(
                    httpContext.GetOrCreateRequestId(),
                    listing.Id,
                    listing.Status,
                    ToWorkerV2Version(listing)));
        },
        cancellationToken);
});

workerV2.MapPatch("/products/{productId}", async (
    HttpContext httpContext,
    string productId,
    WorkerV2ProductUpdateRequest request,
    WorkerDbContext dbContext,
    IdempotencyExecutor idempotency,
    TransientFailureState transientFailureState,
    CancellationToken cancellationToken) =>
{
    EnsureWorkerV2FeatureEnabled(workerV2Features, "products.update");

    if (string.IsNullOrWhiteSpace(productId))
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "productId обязателен.");
    }

    if (!HasWorkerV2ProductChanges(request.Changes))
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "Требуется минимум одно поле в changes.");
    }

    if (request.Changes.Price is not null && request.Changes.Price.Amount < 0)
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "price.amount не может быть отрицательным.");
    }

    if (request.Changes.Quantity is < 0)
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "quantity не может быть отрицательным.");
    }

    ApplyScenarioGuard(runtimeSettings, transientFailureState, "workerV2ProductUpdate");

    var normalizedProductId = productId.Trim();
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    return await idempotency.ExecuteAsync(
        dbContext,
        $"worker:v2:products:update:{normalizedProductId}",
        idempotencyKey,
        async ct =>
        {
            var listing = await dbContext.Listings.SingleOrDefaultAsync(x => x.Id == normalizedProductId, ct);
            if (listing is null)
            {
                throw CreatePlatformError(StatusCodes.Status404NotFound, "Товар не найден.");
            }

            ValidateWorkerV2ExpectedVersion(request.ExpectedVersion, listing);

            if (request.Changes.Title is not null)
            {
                if (string.IsNullOrWhiteSpace(request.Changes.Title))
                {
                    throw CreatePlatformError(StatusCodes.Status400BadRequest, "title не может быть пустым.");
                }

                listing.Title = request.Changes.Title.Trim();
            }

            if (request.Changes.Status is not null)
            {
                if (string.IsNullOrWhiteSpace(request.Changes.Status))
                {
                    throw CreatePlatformError(StatusCodes.Status400BadRequest, "status не может быть пустым.");
                }

                listing.Status = NormalizeProductStatus(request.Changes.Status, listing.Status);
            }

            if (request.Changes.Price is not null)
            {
                listing.Price = decimal.Round(request.Changes.Price.Amount, 2);
            }

            var nextState = v2State.GetOrCreateProductState(normalizedProductId);

            if (request.Changes.Description is not null)
            {
                nextState.Description = request.Changes.Description.Trim();
            }

            if (request.Changes.Quantity is not null)
            {
                nextState.Quantity = request.Changes.Quantity.Value;
            }

            if (request.Changes.Media is not null)
            {
                nextState.Media = request.Changes.Media
                    .Select(x => new WorkerV2MediaItem(x.Type, x.Url))
                    .ToArray();
            }

            if (request.Changes.Attributes is not null)
            {
                nextState.Attributes = request.Changes.Attributes
                    .ToDictionary(x => x.Key, x => x.Value.Clone(), StringComparer.Ordinal);
            }

            if (request.Changes.Price is not null)
            {
                nextState.Currency = NormalizeCurrency(request.Changes.Price.Currency);
            }

            listing.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(ct);
            v2State.SetProductState(normalizedProductId, nextState);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new WorkerV2ProductMutationResponse(
                    httpContext.GetOrCreateRequestId(),
                    listing.Id,
                    listing.Status,
                    ToWorkerV2Version(listing)));
        },
        cancellationToken);
});

workerV2.MapDelete("/products/{productId}", async (
    HttpContext httpContext,
    string productId,
    [FromBody] WorkerV2ProductDeleteRequest? request,
    WorkerDbContext dbContext,
    IdempotencyExecutor idempotency,
    TransientFailureState transientFailureState,
    CancellationToken cancellationToken) =>
{
    EnsureWorkerV2FeatureEnabled(workerV2Features, "products.delete");

    if (string.IsNullOrWhiteSpace(productId))
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "productId обязателен.");
    }

    ApplyScenarioGuard(runtimeSettings, transientFailureState, "workerV2ProductDelete");

    var normalizedProductId = productId.Trim();
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    return await idempotency.ExecuteAsync(
        dbContext,
        $"worker:v2:products:delete:{normalizedProductId}",
        idempotencyKey,
        async ct =>
        {
            var listing = await dbContext.Listings.SingleOrDefaultAsync(x => x.Id == normalizedProductId, ct);
            if (listing is null)
            {
                throw CreatePlatformError(StatusCodes.Status404NotFound, "Товар не найден.");
            }

            ValidateWorkerV2ExpectedVersion(request?.ExpectedVersion, listing);

            var mode = (request?.Mode ?? "soft").Trim().ToLowerInvariant();
            if (mode is not ("soft" or "hard"))
            {
                throw CreatePlatformError(StatusCodes.Status400BadRequest, "mode должен быть soft или hard.");
            }

            if (mode == "hard")
            {
                dbContext.Listings.Remove(listing);
                await dbContext.SaveChangesAsync(ct);
                v2State.RemoveProductState(normalizedProductId);

                return new IdempotentExecutionResult(
                    StatusCodes.Status200OK,
                    new WorkerV2ProductMutationResponse(
                        httpContext.GetOrCreateRequestId(),
                        normalizedProductId,
                        "deleted",
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()));
            }

            listing.Status = "archived";
            listing.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new WorkerV2ProductMutationResponse(
                    httpContext.GetOrCreateRequestId(),
                    listing.Id,
                    listing.Status,
                    ToWorkerV2Version(listing)));
        },
        cancellationToken);
});

workerV2.MapGet("/schemas/products", (
    HttpContext httpContext,
    string? schemaId,
    string? provider,
    TransientFailureState transientFailureState) =>
{
    ApplyScenarioGuard(runtimeSettings, transientFailureState, "workerV2ProductSchemas");

    var requestedProvider = string.IsNullOrWhiteSpace(provider)
        ? runtimeSettings.Provider
        : NormalizeWorkerV2ProviderFromRequest(provider);

    var schemas = GetWorkerV2ProductSchemas(requestedProvider);
    if (!string.IsNullOrWhiteSpace(schemaId))
    {
        schemas = schemas
            .Where(x => string.Equals(x.SchemaId, schemaId.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    return Results.Ok(new WorkerV2ProductSchemasResponse(httpContext.GetOrCreateRequestId(), schemas));
});

app.Run();

static string ResolveWorkerInstanceId(IConfiguration configuration)
{
    var configured = configuration["TEST_WORKER_INSTANCE_ID"]?.Trim();
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return configured;
    }

    return Environment.MachineName.Trim();
}

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

    var provider = NormalizeWorkerV2Provider(options.Provider);

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
        provider,
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

static (Guid AccountId, ProxyConfigValue ProxyConfig) ReadProxyCredentialsApplyPayload(ExtensionActionRequest? request)
{
    if (request?.Payload is null)
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "Для apply требуется payload.");
    }

    var accountId = ReadRequiredGuid(request.Payload, "accountId");

    if (!request.Payload.TryGetValue("proxyConfig", out var proxyConfigElement) || proxyConfigElement.ValueKind != JsonValueKind.Object)
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "Для apply требуется payload.proxyConfig.");
    }

    var proxyConfigPayload = proxyConfigElement.EnumerateObject()
        .ToDictionary(x => x.Name, x => x.Value.Clone(), StringComparer.Ordinal);

    var host = ReadRequiredString(proxyConfigPayload, "host");
    var login = ReadRequiredString(proxyConfigPayload, "login");
    var password = ReadRequiredString(proxyConfigPayload, "password");
    var port = ReadRequiredPort(proxyConfigPayload, "port");

    return (accountId, new ProxyConfigValue(host, port, login, password));
}

static Guid ReadProxyCredentialsRevealAccountId(ExtensionActionRequest? request)
{
    if (request?.Payload is null)
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "Для reveal требуется payload.");
    }

    return ReadRequiredGuid(request.Payload, "accountId");
}

static (Guid AccountId, MarketplaceAuthValue MarketplaceAuth) ReadMarketplaceAuthApplyPayload(ExtensionActionRequest? request)
{
    if (request?.Payload is null)
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "Для marketplace auth apply требуется payload.");
    }

    var accountId = ReadRequiredGuid(request.Payload, "accountId");
    if (!request.Payload.TryGetValue("marketplaceAuth", out var authElement) || authElement.ValueKind != JsonValueKind.Object)
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "Для marketplace auth apply требуется payload.marketplaceAuth.");
    }

    var marketplaceAuthPayload = authElement.EnumerateObject()
        .ToDictionary(x => x.Name, x => x.Value.Clone(), StringComparer.Ordinal);
    var scheme = ReadRequiredString(marketplaceAuthPayload, "scheme");
    if (!MarketplaceAuthSchemeKeys.All.Contains(scheme))
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "payload.marketplaceAuth.scheme содержит неподдерживаемое значение.");
    }

    if (!marketplaceAuthPayload.TryGetValue("credentials", out var credentialsElement) || credentialsElement.ValueKind != JsonValueKind.Object)
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "Для marketplace auth apply требуется payload.marketplaceAuth.credentials.");
    }

    var credentials = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var property in credentialsElement.EnumerateObject())
    {
        if (property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()))
        {
            throw CreatePlatformError(
                StatusCodes.Status400BadRequest,
                $"payload.marketplaceAuth.credentials.{property.Name} должен быть непустой строкой.");
        }

        credentials[property.Name] = property.Value.GetString()!.Trim();
    }

    if (credentials.Count == 0)
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "payload.marketplaceAuth.credentials должен содержать минимум одно значение.");
    }

    if (string.Equals(scheme, MarketplaceAuthSchemeKeys.GoldenKey, StringComparison.Ordinal)
        && !credentials.ContainsKey("golden_key"))
    {
        throw CreatePlatformError(
            StatusCodes.Status400BadRequest,
            "Для marketplaceAuth.scheme=golden_key требуется payload.marketplaceAuth.credentials.golden_key.");
    }
    if (string.Equals(scheme, MarketplaceAuthSchemeKeys.Tokens, StringComparison.Ordinal))
    {
        if (!credentials.ContainsKey("token"))
        {
            throw CreatePlatformError(
                StatusCodes.Status400BadRequest,
                "Для marketplaceAuth.scheme=tokens требуется payload.marketplaceAuth.credentials.token.");
        }
        if (!credentials.ContainsKey("ddg5"))
        {
            throw CreatePlatformError(
                StatusCodes.Status400BadRequest,
                "Для marketplaceAuth.scheme=tokens требуется payload.marketplaceAuth.credentials.ddg5.");
        }
    }
    if (string.Equals(scheme, MarketplaceAuthSchemeKeys.Cookies, StringComparison.Ordinal)
        && !credentials.ContainsKey("cookies"))
    {
        throw CreatePlatformError(
            StatusCodes.Status400BadRequest,
            "Для marketplaceAuth.scheme=cookies требуется payload.marketplaceAuth.credentials.cookies.");
    }

    return (accountId, new MarketplaceAuthValue(scheme, credentials));
}

static Guid ReadRequiredGuid(IDictionary<string, JsonElement> payload, string key)
{
    if (!payload.TryGetValue(key, out var value))
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, $"Поле {key} обязательно.");
    }

    var guidValue = value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Null => null,
        _ => value.GetRawText(),
    };

    if (!Guid.TryParse(guidValue, out var parsed))
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, $"Поле {key} должно быть GUID.");
    }

    return parsed;
}

static string ReadRequiredString(IDictionary<string, JsonElement> payload, string key)
{
    if (!payload.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, $"Поле {key} обязательно и должно быть непустой строкой.");
    }

    return value.GetString()!.Trim();
}

static int ReadRequiredPort(IDictionary<string, JsonElement> payload, string key)
{
    if (!payload.TryGetValue(key, out var value))
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, $"Поле {key} обязательно.");
    }

    var port = value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetInt32(out var intPort) => intPort,
        JsonValueKind.String when int.TryParse(value.GetString(), out var stringPort) => stringPort,
        _ => throw CreatePlatformError(StatusCodes.Status400BadRequest, $"Поле {key} должно быть числом."),
    };

    if (port is < 1 or > 65535)
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, $"Поле {key} должно быть в диапазоне 1..65535.");
    }

    return port;
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

static byte[] ResolveProxyCredentialsEncryptionKey(string? rawKey)
{
    var source = string.IsNullOrWhiteSpace(rawKey)
        ? "ddcrm-local-worker-proxy-credentials-key"
        : rawKey.Trim();

    return SHA256.HashData(Encoding.UTF8.GetBytes(source));
}

static string EncryptProxySecret(string plaintext, byte[] key)
{
    var nonce = RandomNumberGenerator.GetBytes(12);
    var plainBytes = Encoding.UTF8.GetBytes(plaintext);
    var cipherBytes = new byte[plainBytes.Length];
    var tag = new byte[16];

    using var aes = new AesGcm(key, tag.Length);
    aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

    var output = new byte[nonce.Length + tag.Length + cipherBytes.Length];
    Buffer.BlockCopy(nonce, 0, output, 0, nonce.Length);
    Buffer.BlockCopy(tag, 0, output, nonce.Length, tag.Length);
    Buffer.BlockCopy(cipherBytes, 0, output, nonce.Length + tag.Length, cipherBytes.Length);

    return Convert.ToBase64String(output);
}

static string DecryptProxySecret(string encodedCiphertext, byte[] key)
{
    try
    {
        var input = Convert.FromBase64String(encodedCiphertext);
        if (input.Length < 29)
        {
            throw new ApiErrorException(
                StatusCodes.Status500InternalServerError,
                WorkerErrorCodes.PlatformError,
                "Повреждённое proxy credential значение в worker storage.");
        }

        var nonce = input.AsSpan(0, 12).ToArray();
        var tag = input.AsSpan(12, 16).ToArray();
        var cipher = input.AsSpan(28).ToArray();
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }
    catch (ApiErrorException)
    {
        throw;
    }
    catch (Exception)
    {
        throw new ApiErrorException(
            StatusCodes.Status500InternalServerError,
            WorkerErrorCodes.PlatformError,
            "Не удалось расшифровать proxy credentials из worker storage.");
    }
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

static string NormalizeProductStatus(string? status, string fallback)
{
    var normalized = string.IsNullOrWhiteSpace(status) ? fallback : status.Trim();
    if (string.IsNullOrWhiteSpace(normalized))
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "status не может быть пустым.");
    }

    return normalized;
}

static string NormalizeCurrency(string? currency)
{
    if (string.IsNullOrWhiteSpace(currency))
    {
        return "RUB";
    }

    var normalized = currency.Trim().ToUpperInvariant();
    if (normalized.Length is < 3 or > 8)
    {
        throw CreatePlatformError(StatusCodes.Status400BadRequest, "currency должен быть длиной от 3 до 8 символов.");
    }

    return normalized;
}

static string ToWorkerV2Version(WorkerListingEntity listing) =>
    listing.UpdatedAtUtc.ToUnixTimeMilliseconds().ToString();

static void ValidateWorkerV2ExpectedVersion(string? expectedVersion, WorkerListingEntity listing)
{
    if (string.IsNullOrWhiteSpace(expectedVersion))
    {
        return;
    }

    var currentVersion = ToWorkerV2Version(listing);
    if (!string.Equals(expectedVersion.Trim(), currentVersion, StringComparison.Ordinal))
    {
        throw CreateRuntimeConflict("Версия товара не совпадает. Требуется актуальная версия для изменения.");
    }
}

static bool HasWorkerV2ProductChanges(WorkerV2ProductChanges changes) =>
    changes.Title is not null
    || changes.Description is not null
    || changes.Price is not null
    || changes.Status is not null
    || changes.Quantity is not null
    || changes.Media is not null
    || changes.Attributes is not null;

static void ValidateWorkerV2ProductCreateSchema(string provider, WorkerV2ProductCreateRequest request)
{
    var schemas = GetWorkerV2ProductSchemas(provider);
    var schema = schemas.FirstOrDefault(x =>
        string.Equals(x.SchemaId, request.SchemaId.Trim(), StringComparison.OrdinalIgnoreCase));

    if (schema is null)
    {
        throw CreatePlatformError(
            StatusCodes.Status400BadRequest,
            $"Неизвестный schemaId `{request.SchemaId}` для provider `{provider}`.");
    }

    foreach (var requiredField in schema.Fields.Where(x => x.Required))
    {
        if (HasWorkerV2RequiredCreateField(request, requiredField.Key))
        {
            continue;
        }

        throw CreatePlatformError(
            StatusCodes.Status400BadRequest,
            $"Для schemaId `{schema.SchemaId}` обязательно поле `{requiredField.Key}`.");
    }
}

static bool HasWorkerV2RequiredCreateField(WorkerV2ProductCreateRequest request, string fieldKey)
{
    return fieldKey switch
    {
        "schemaId" => !string.IsNullOrWhiteSpace(request.SchemaId),
        "title" => !string.IsNullOrWhiteSpace(request.Title),
        "description" => !string.IsNullOrWhiteSpace(request.Description),
        "price.amount" => true,
        "price.currency" => !string.IsNullOrWhiteSpace(request.Price.Currency),
        "status" => !string.IsNullOrWhiteSpace(request.Status),
        "quantity" => request.Quantity is not null,
        "media" => request.Media is { Count: > 0 },
        _ when fieldKey.StartsWith("attributes.", StringComparison.Ordinal) => HasWorkerV2RequiredAttribute(
            request.Attributes,
            fieldKey["attributes.".Length..]),
        _ => false,
    };
}

static bool HasWorkerV2RequiredAttribute(Dictionary<string, JsonElement>? attributes, string key)
{
    if (attributes is null || string.IsNullOrWhiteSpace(key))
    {
        return false;
    }

    if (!TryGetAttributeValue(attributes, key, out var value))
    {
        return false;
    }

    if (value.ValueKind == JsonValueKind.Null)
    {
        return false;
    }

    if (value.ValueKind == JsonValueKind.String)
    {
        return !string.IsNullOrWhiteSpace(value.GetString());
    }

    return true;
}

static bool TryGetAttributeValue(Dictionary<string, JsonElement> attributes, string key, out JsonElement value)
{
    if (attributes.TryGetValue(key, out value))
    {
        return true;
    }

    foreach (var candidate in attributes)
    {
        if (string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase))
        {
            value = candidate.Value;
            return true;
        }
    }

    value = default;
    return false;
}

static WorkerV2Product ToWorkerV2Product(WorkerListingEntity listing, WorkerV2ProductState state)
{
    var media = state.Media
        .Select(x => new WorkerV2MediaItem(x.Type, x.Url))
        .ToArray();

    var attributes = state.Attributes
        .ToDictionary(x => x.Key, x => x.Value.Clone(), StringComparer.Ordinal);

    return new WorkerV2Product(
        listing.Id,
        listing.Title,
        state.Description,
        new WorkerV2Money(listing.Price, state.Currency),
        listing.Status,
        state.Quantity,
        media,
        attributes,
        ToWorkerV2Version(listing),
        state.SchemaId);
}

static IReadOnlyDictionary<string, bool> ResolveWorkerV2FeatureMap(string provider) => provider switch
{
    WorkerV2ProviderAliases.GgSell => new Dictionary<string, bool>(StringComparer.Ordinal)
    {
        ["account.info"] = true,
        ["conversations.list"] = false,
        ["conversations.messages.list"] = false,
        ["conversations.messages.send"] = false,
        ["products.list"] = true,
        ["products.create"] = false,
        ["products.update"] = false,
        ["products.delete"] = false,
    },
    _ => new Dictionary<string, bool>(StringComparer.Ordinal)
    {
        ["account.info"] = true,
        ["conversations.list"] = true,
        ["conversations.messages.list"] = true,
        ["conversations.messages.send"] = true,
        ["products.list"] = true,
        ["products.create"] = true,
        ["products.update"] = true,
        ["products.delete"] = true,
    },
};

static void EnsureWorkerV2FeatureEnabled(IReadOnlyDictionary<string, bool> features, string featureKey)
{
    if (features.TryGetValue(featureKey, out var enabled) && enabled)
    {
        return;
    }

    throw CreateRuntimeConflict($"Операция `{featureKey}` отключена активным provider feature-map.");
}

static string NormalizeWorkerV2Provider(string? provider)
{
    var normalized = string.IsNullOrWhiteSpace(provider)
        ? WorkerV2ProviderAliases.PlatiMarket
        : provider.Trim().ToLowerInvariant();

    return WorkerV2ProviderAliases.All.Contains(normalized, StringComparer.Ordinal)
        ? normalized
        : throw new InvalidOperationException(
            $"Неизвестный TEST_WORKER_PROVIDER: {provider}. Допустимые значения: {string.Join(", ", WorkerV2ProviderAliases.All)}.");
}

static string NormalizeWorkerV2ProviderFromRequest(string provider)
{
    var normalized = provider.Trim().ToLowerInvariant();
    if (WorkerV2ProviderAliases.All.Contains(normalized, StringComparer.Ordinal))
    {
        return normalized;
    }

    throw CreatePlatformError(
        StatusCodes.Status400BadRequest,
        $"Неизвестный provider. Допустимые значения: {string.Join(", ", WorkerV2ProviderAliases.All)}.");
}

static WorkerV2AccountDescriptor ResolveWorkerV2AccountDescriptor(string provider) => provider switch
{
    WorkerV2ProviderAliases.FunPay => new WorkerV2AccountDescriptor(
        AccountId: "11111111-1111-1111-1111-111111111111",
        Nickname: "ddcrm_funpay_demo",
        DisplayName: "DDCRM FunPay Demo Account",
        Balance: 850.00m),
    WorkerV2ProviderAliases.Playerok => new WorkerV2AccountDescriptor(
        AccountId: "11111111-1111-1111-1111-111111111111",
        Nickname: "ddcrm_playerok_demo",
        DisplayName: "DDCRM Playerok Demo Account",
        Balance: 1120.00m),
    WorkerV2ProviderAliases.GgSell => new WorkerV2AccountDescriptor(
        AccountId: "11111111-1111-1111-1111-111111111111",
        Nickname: "ddcrm_ggsell_demo",
        DisplayName: "DDCRM GGSell Demo Account",
        Balance: 540.00m),
    _ => new WorkerV2AccountDescriptor(
        AccountId: "11111111-1111-1111-1111-111111111111",
        Nickname: "ddcrm_platimarket_demo",
        DisplayName: "DDCRM PlatiMarket Demo Account",
        Balance: 1000.00m),
};

static IReadOnlyCollection<WorkerV2ProductSchema> GetWorkerV2ProductSchemas(string provider)
{
    var common = new[]
    {
        new WorkerV2ProductSchema(
            "digital_goods.v1",
            provider,
            [
                new WorkerV2ProductSchemaField("title", "string", true),
                new WorkerV2ProductSchemaField("price.amount", "number", true),
                new WorkerV2ProductSchemaField("price.currency", "string", true),
                new WorkerV2ProductSchemaField("quantity", "integer", false),
                new WorkerV2ProductSchemaField("attributes.region", "string", false),
            ]),
    };

    var providerSpecific = provider switch
    {
        WorkerV2ProviderAliases.FunPay => new[]
        {
            new WorkerV2ProductSchema(
                "funpay.item.v1",
                provider,
                [
                    new WorkerV2ProductSchemaField("title", "string", true),
                    new WorkerV2ProductSchemaField("price.amount", "number", true),
                    new WorkerV2ProductSchemaField("attributes.nodeId", "string", true),
                    new WorkerV2ProductSchemaField("attributes.autoDelivery", "boolean", false),
                ]),
        },
        WorkerV2ProviderAliases.Playerok => new[]
        {
            new WorkerV2ProductSchema(
                "playerok.item.v1",
                provider,
                [
                    new WorkerV2ProductSchemaField("title", "string", true),
                    new WorkerV2ProductSchemaField("price.amount", "number", true),
                    new WorkerV2ProductSchemaField("attributes.gameCategoryId", "string", true),
                    new WorkerV2ProductSchemaField("attributes.obtainingTypeId", "string", false),
                    new WorkerV2ProductSchemaField("media", "array", false),
                ]),
        },
        WorkerV2ProviderAliases.GgSell => new[]
        {
            new WorkerV2ProductSchema(
                "ggsell.item.v1",
                provider,
                [
                    new WorkerV2ProductSchemaField("title", "string", true),
                    new WorkerV2ProductSchemaField("price.amount", "number", true),
                    new WorkerV2ProductSchemaField("attributes.categoryId", "string", true),
                    new WorkerV2ProductSchemaField("attributes.paymentMethods", "array", false),
                ]),
        },
        _ => new[]
        {
            new WorkerV2ProductSchema(
                "platimarket.item.v1",
                provider,
                [
                    new WorkerV2ProductSchemaField("title", "string", true),
                    new WorkerV2ProductSchemaField("price.amount", "number", true),
                    new WorkerV2ProductSchemaField("attributes.categoryId", "string", false),
                    new WorkerV2ProductSchemaField("attributes.partnerData", "object", false),
                    new WorkerV2ProductSchemaField("media", "array", false),
                ]),
        },
    };

    return common.Concat(providerSpecific).ToArray();
}

public sealed record WorkerRuntimeSettings(
    bool TestWorkerEnabled,
    string Provider,
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

internal static class WorkerExtensionActionKeys
{
    public const string ProxyCredentialsApply = "ext.account.proxy-credentials.apply";
    public const string ProxyCredentialsReveal = "ext.account.proxy-credentials.reveal";
    public const string MarketplaceAuthApply = "ext.account.marketplace-auth.apply";
}

internal sealed record ProxyConfigValue(string Host, int Port, string Login, string Password);

internal sealed record MarketplaceAuthValue(string Scheme, IReadOnlyDictionary<string, string> Credentials);

internal static class MarketplaceAuthSchemeKeys
{
    public const string GoldenKey = "golden_key";
    public const string Cookies = "cookies";
    public const string Tokens = "tokens";
    public const string LoginPassword = "login_password";

    public static readonly HashSet<string> All = new(
        [GoldenKey, Cookies, Tokens, LoginPassword],
        StringComparer.Ordinal);
}

internal static class WorkerV2ProviderAliases
{
    public const string FunPay = "funpay";
    public const string Playerok = "playerok";
    public const string GgSell = "ggsell";
    public const string PlatiMarket = "platimarket";

    public static readonly string[] All = [FunPay, Playerok, GgSell, PlatiMarket];
}

internal sealed class WorkerV2State
{
    private readonly object _sync = new();
    private readonly Dictionary<string, WorkerV2ConversationThread> _conversations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkerV2ProductState> _products = new(StringComparer.Ordinal);

    private WorkerV2State()
    {
    }

    public static WorkerV2State CreateDefault()
    {
        var state = new WorkerV2State();

        state._conversations["conv-100"] = new WorkerV2ConversationThread
        {
            ConversationId = "conv-100",
            PeerId = "buyer-100",
            PeerName = "buyer_funpay_demo",
            UnreadCount = 1,
            Messages =
            [
                new WorkerV2ConversationMessage(
                    "msg-100-1",
                    "in",
                    "Здравствуйте, товар еще доступен?",
                    [],
                    DateTimeOffset.UtcNow.AddMinutes(-30)),
                new WorkerV2ConversationMessage(
                    "msg-100-2",
                    "out",
                    "Да, товар доступен.",
                    [],
                    DateTimeOffset.UtcNow.AddMinutes(-28)),
            ],
        };

        state._conversations["conv-200"] = new WorkerV2ConversationThread
        {
            ConversationId = "conv-200",
            PeerId = "buyer-200",
            PeerName = "buyer_playerok_demo",
            UnreadCount = 0,
            Messages =
            [
                new WorkerV2ConversationMessage(
                    "msg-200-1",
                    "in",
                    "Можно небольшую скидку?",
                    [],
                    DateTimeOffset.UtcNow.AddMinutes(-15)),
            ],
        };

        return state;
    }

    public IReadOnlyCollection<WorkerV2ConversationSummary> GetConversationSummaries(bool onlyUnread)
    {
        lock (_sync)
        {
            return _conversations.Values
                .Where(x => !onlyUnread || x.UnreadCount > 0)
                .Select(x =>
                {
                    var last = x.Messages.Count == 0
                        ? null
                        : x.Messages.OrderBy(y => y.CreatedAt).Last();

                    return new WorkerV2ConversationSummary(
                        x.ConversationId,
                        x.PeerId,
                        x.PeerName,
                        x.UnreadCount,
                        last?.Text,
                        last?.CreatedAt ?? DateTimeOffset.UtcNow);
                })
                .ToArray();
        }
    }

    public IReadOnlyCollection<WorkerV2ConversationMessage> GetConversationMessages(string conversationId)
    {
        lock (_sync)
        {
            if (!_conversations.TryGetValue(conversationId, out var conversation))
            {
                throw new ApiErrorException(
                    StatusCodes.Status404NotFound,
                    WorkerErrorCodes.PlatformError,
                    "Переписка не найдена.");
            }

            return conversation.Messages
                .Select(CloneMessage)
                .ToArray();
        }
    }

    public WorkerV2ConversationMessage AddOutgoingMessage(
        string conversationId,
        string text,
        IReadOnlyCollection<WorkerV2MessageAttachment> attachments)
    {
        lock (_sync)
        {
            if (!_conversations.TryGetValue(conversationId, out var conversation))
            {
                conversation = new WorkerV2ConversationThread
                {
                    ConversationId = conversationId,
                    PeerId = "buyer-new",
                    PeerName = "new_buyer",
                    UnreadCount = 0,
                    Messages = [],
                };
                _conversations[conversationId] = conversation;
            }

            var message = new WorkerV2ConversationMessage(
                $"msg-{Guid.NewGuid():N}"[..20],
                "out",
                text,
                attachments.Select(CloneAttachment).ToArray(),
                DateTimeOffset.UtcNow);

            conversation.Messages.Add(message);
            return CloneMessage(message);
        }
    }

    public WorkerV2ProductState GetOrCreateProductState(string productId)
    {
        lock (_sync)
        {
            if (!_products.TryGetValue(productId, out var state))
            {
                state = WorkerV2ProductState.CreateDefault();
                _products[productId] = state;
            }

            return state.Clone();
        }
    }

    public void SetProductState(string productId, WorkerV2ProductState state)
    {
        lock (_sync)
        {
            _products[productId] = state.Clone();
        }
    }

    public void RemoveProductState(string productId)
    {
        lock (_sync)
        {
            _products.Remove(productId);
        }
    }

    private static WorkerV2ConversationMessage CloneMessage(WorkerV2ConversationMessage source) =>
        new(
            source.MessageId,
            source.Direction,
            source.Text,
            source.Attachments.Select(CloneAttachment).ToArray(),
            source.CreatedAt);

    private static WorkerV2MessageAttachment CloneAttachment(WorkerV2MessageAttachment source) =>
        new(source.Type, source.Url);
}

internal sealed class WorkerV2ConversationThread
{
    public required string ConversationId { get; init; }
    public required string PeerId { get; init; }
    public required string PeerName { get; init; }
    public required List<WorkerV2ConversationMessage> Messages { get; init; }
    public int UnreadCount { get; set; }
}

public sealed record WorkerV2AccountInfo(
    string Provider,
    string AccountId,
    string Nickname,
    string Status,
    IReadOnlyDictionary<string, object?> Profile,
    IReadOnlyDictionary<string, object?> Raw);

public sealed record WorkerV2AccountInfoResponse(string RequestId, WorkerV2AccountInfo Account);

public sealed record WorkerV2CapabilitiesResponse(
    string RequestId,
    string Provider,
    IReadOnlyDictionary<string, bool> Features,
    IReadOnlyCollection<CapabilityItem>? Capabilities = null);

public sealed record WorkerV2ConversationSummary(
    string ConversationId,
    string PeerId,
    string PeerName,
    int UnreadCount,
    string? LastMessagePreview,
    DateTimeOffset LastMessageAt);

public sealed record WorkerV2ConversationsResponse(
    string RequestId,
    IReadOnlyCollection<WorkerV2ConversationSummary> Items,
    string? NextCursor);

public sealed record WorkerV2MessageAttachment(string Type, string? Url);

public sealed record WorkerV2ConversationMessage(
    string MessageId,
    string Direction,
    string Text,
    IReadOnlyCollection<WorkerV2MessageAttachment> Attachments,
    DateTimeOffset CreatedAt);

public sealed record WorkerV2ConversationMessagesResponse(
    string RequestId,
    IReadOnlyCollection<WorkerV2ConversationMessage> Items,
    string? NextCursor);

public sealed record WorkerV2MessageSendRequest(
    string Text,
    IReadOnlyCollection<WorkerV2MessageAttachment>? Attachments);

public sealed record WorkerV2MessageSendResponse(
    string RequestId,
    string MessageId,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record WorkerV2Money(decimal Amount, string Currency);

public sealed record WorkerV2MediaItem(string Type, string Url);

public sealed record WorkerV2Product(
    string ProductId,
    string Title,
    string? Description,
    WorkerV2Money Price,
    string Status,
    int Quantity,
    IReadOnlyCollection<WorkerV2MediaItem> Media,
    IReadOnlyDictionary<string, JsonElement> Attributes,
    string Version,
    string SchemaId);

public sealed record WorkerV2ProductsResponse(
    string RequestId,
    IReadOnlyCollection<WorkerV2Product> Items,
    string? NextCursor);

public sealed record WorkerV2ProductCreateRequest(
    string SchemaId,
    string Title,
    string? Description,
    WorkerV2Money Price,
    string? Status,
    int? Quantity,
    IReadOnlyCollection<WorkerV2MediaItem>? Media,
    Dictionary<string, JsonElement>? Attributes);

public sealed record WorkerV2ProductChanges(
    string? Title,
    string? Description,
    WorkerV2Money? Price,
    string? Status,
    int? Quantity,
    IReadOnlyCollection<WorkerV2MediaItem>? Media,
    Dictionary<string, JsonElement>? Attributes);

public sealed record WorkerV2ProductUpdateRequest(string? ExpectedVersion, WorkerV2ProductChanges Changes);

public sealed record WorkerV2ProductDeleteRequest(string? Mode, string? Reason, string? ExpectedVersion);

public sealed record WorkerV2ProductMutationResponse(
    string RequestId,
    string ProductId,
    string Status,
    string Version);

public sealed record WorkerV2ProductSchemaField(string Key, string Type, bool Required);

public sealed record WorkerV2ProductSchema(
    string SchemaId,
    string Provider,
    IReadOnlyCollection<WorkerV2ProductSchemaField> Fields);

public sealed record WorkerV2ProductSchemasResponse(string RequestId, IReadOnlyCollection<WorkerV2ProductSchema> Items);

internal sealed record WorkerV2AccountDescriptor(
    string AccountId,
    string Nickname,
    string DisplayName,
    decimal Balance);

public sealed class WorkerV2ProductState
{
    public string? Description { get; set; }
    public string Currency { get; set; } = "RUB";
    public int Quantity { get; set; } = 1;
    public string SchemaId { get; set; } = "digital_goods.v1";
    public IReadOnlyCollection<WorkerV2MediaItem> Media { get; set; } = [];
    public Dictionary<string, JsonElement> Attributes { get; set; } = new(StringComparer.Ordinal);

    public static WorkerV2ProductState CreateDefault() => new()
    {
        Description = null,
        Currency = "RUB",
        Quantity = 1,
        SchemaId = "digital_goods.v1",
        Media = [],
        Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal),
    };

    public static WorkerV2ProductState FromRequest(WorkerV2ProductCreateRequest request)
    {
        var currency = string.IsNullOrWhiteSpace(request.Price.Currency)
            ? "RUB"
            : request.Price.Currency.Trim().ToUpperInvariant();

        return new WorkerV2ProductState
        {
            Description = request.Description?.Trim(),
            Currency = currency,
            Quantity = request.Quantity ?? 1,
            SchemaId = request.SchemaId.Trim(),
            Media = request.Media?.Select(x => new WorkerV2MediaItem(x.Type, x.Url)).ToArray() ?? [],
            Attributes = request.Attributes?
                .ToDictionary(x => x.Key, x => x.Value.Clone(), StringComparer.Ordinal)
                ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal),
        };
    }

    public WorkerV2ProductState Clone() => new()
    {
        Description = Description,
        Currency = Currency,
        Quantity = Quantity,
        SchemaId = SchemaId,
        Media = Media.Select(x => new WorkerV2MediaItem(x.Type, x.Url)).ToArray(),
        Attributes = Attributes.ToDictionary(x => x.Key, x => x.Value.Clone(), StringComparer.Ordinal),
    };
}

public partial class Program;
