using System.Text.Json;
using DDCRM.Billing.Api.Entitlement;
using DDCRM.Billing.Persistence;
using DDCRM.Billing.Persistence.Entities;
using DDCRM.Shared.Auth;
using DDCRM.Shared.Errors;
using DDCRM.Shared.Extensions;
using DDCRM.Shared.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<BillingDbContext>((serviceProvider, options) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var useInMemoryDb = configuration.GetValue("TEST_USE_INMEMORY_DB", false);

    if (useInMemoryDb)
    {
        options.UseInMemoryDatabase(configuration["TEST_INMEMORY_DB_NAME"] ?? "ddcrm-billing-tests");
        return;
    }

    options.UseNpgsql(
        configuration.GetConnectionString("BillingDb")
        ?? configuration["BILLING_DB_CONNECTION"]
        ?? "Host=localhost;Port=5432;Database=ddcrm_billing;Username=postgres;Password=postgres");
});

builder.Services.AddScoped<IdempotencyExecutor>();
builder.Services.Configure<EntitlementClientOptions>(builder.Configuration.GetSection(EntitlementClientOptions.SectionName));
builder.Services.PostConfigure<EntitlementClientOptions>(options =>
{
    options.ServiceToken ??= builder.Configuration["INTERNAL_API_SERVICE_AUTH_CLIENT_TOKEN"];
});

builder.Services.AddHttpClient<IEntitlementClient, EntitlementHttpClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<EntitlementClientOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});

builder.Services.AddServiceTokenAuth(options =>
{
    options.Enabled = builder.Configuration.GetValue("INTERNAL_API_SERVICE_AUTH_ENABLED", true);
    options.AcceptedTokens = builder.Configuration.GetCommaSeparatedValues("INTERNAL_API_SERVICE_AUTH_ACCEPTED_TOKENS");
    options.ForbiddenTokens = builder.Configuration.GetCommaSeparatedValues("WORKER_API_SERVICE_AUTH_ACCEPTED_TOKENS");
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
    if (dbContext.Database.IsRelational())
    {
        dbContext.Database.Migrate();
    }
    else
    {
        dbContext.Database.EnsureCreated();
    }
}

app.UseDdcrmCommonPipeline();
app.UseServiceTokenAuth();

app.MapPost("/internal/v1/payments/create", async (
    HttpContext httpContext,
    Dictionary<string, JsonElement> request,
    BillingDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    var projectId = ReadGuid(request, "projectId");
    var amount = ReadDecimal(request, "amount", required: true);

    if (amount is null || amount <= 0)
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "amount должен быть больше нуля.");
    }

    var currency = ReadOptionalString(request, "currency") ?? "USD";
    var planKey = ReadOptionalString(request, "planKey");
    var sourceOperation = ReadOptionalString(request, "operation") ?? "payment.create";

    return await idempotency.ExecuteAsync(
        dbContext,
        $"billing:payments:create:{projectId}",
        idempotencyKey,
        async ct =>
        {
            var payment = new BillingPaymentEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Status = "pending",
                Amount = decimal.Round(amount.Value, 2),
                Currency = currency,
                ProviderPaymentId = $"pay-{Guid.NewGuid():N}"[..20],
                PlanKey = planKey,
                SourceOperation = sourceOperation,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };

            dbContext.Payments.Add(payment);
            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(
                StatusCodes.Status200OK,
                new GenericObjectResponse(httpContext.GetOrCreateRequestId(), new Dictionary<string, object?>
                {
                    ["paymentId"] = payment.Id,
                    ["providerPaymentId"] = payment.ProviderPaymentId,
                    ["projectId"] = payment.ProjectId,
                    ["status"] = payment.Status,
                    ["amount"] = payment.Amount,
                    ["currency"] = payment.Currency,
                    ["planKey"] = payment.PlanKey,
                }));
        },
        cancellationToken);
});

app.MapPost("/internal/v1/payments/webhook", async (
    HttpContext httpContext,
    Dictionary<string, JsonElement> request,
    BillingDbContext dbContext,
    IEntitlementClient entitlementClient,
    CancellationToken cancellationToken) =>
{
    var eventId = ReadString(request, "eventId");
    var eventType = ReadString(request, "eventType");
    var projectId = TryReadGuid(request, "projectId");
    var paymentId = TryReadGuid(request, "paymentId");
    var now = DateTimeOffset.UtcNow;

    var duplicate = await dbContext.WebhookEvents
        .AsNoTracking()
        .AnyAsync(x => x.EventId == eventId, cancellationToken);

    if (duplicate)
    {
        return Results.Ok(new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
    }

    var payment = paymentId is null
        ? null
        : await dbContext.Payments.SingleOrDefaultAsync(x => x.Id == paymentId, cancellationToken);

    var effectiveProjectId = payment?.ProjectId ?? projectId;
    if (effectiveProjectId is null)
    {
        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            "projectId обязателен, если paymentId не найден.");
    }

    var shouldRecalculateEntitlement = false;
    var entitlementStatus = string.Empty;
    var entitlementPlanKey = string.Empty;
    var entitlementSource = $"billing.webhook:{eventType}";

    switch (eventType)
    {
        case "payment.succeeded":
            if (payment is not null)
            {
                payment.Status = "succeeded";
                payment.UpdatedAtUtc = now;
            }

            await UpsertSubscriptionAsync(
                dbContext,
                effectiveProjectId.Value,
                status: "active",
                planKey: ReadOptionalString(request, "planKey") ?? payment?.PlanKey ?? "basic",
                currentPeriodEndsAtUtc: now.AddDays(30),
                graceEndsAtUtc: null,
                now,
                cancellationToken);

            shouldRecalculateEntitlement = true;
            entitlementStatus = "active";
            entitlementPlanKey = ReadOptionalString(request, "planKey") ?? payment?.PlanKey ?? "basic";

            dbContext.Audits.Add(CreateAudit(
                effectiveProjectId.Value,
                "webhook.payment.succeeded",
                "billing-webhook",
                "payment succeeded webhook processed"));
            break;

        case "payment.failed":
            if (payment is not null)
            {
                payment.Status = "failed";
                payment.UpdatedAtUtc = now;
            }

            await UpsertSubscriptionAsync(
                dbContext,
                effectiveProjectId.Value,
                status: "grace",
                planKey: ReadOptionalString(request, "planKey") ?? payment?.PlanKey ?? "basic",
                currentPeriodEndsAtUtc: now,
                graceEndsAtUtc: now.AddDays(7),
                now,
                cancellationToken);

            shouldRecalculateEntitlement = true;
            entitlementStatus = "grace";
            entitlementPlanKey = ReadOptionalString(request, "planKey") ?? payment?.PlanKey ?? "basic";

            dbContext.Audits.Add(CreateAudit(
                effectiveProjectId.Value,
                "webhook.payment.failed",
                "billing-webhook",
                "payment failed webhook processed"));
            break;

        case "refund.succeeded":
            if (payment is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Платеж для refund webhook не найден.");
            }

            payment.Status = "refunded";
            payment.UpdatedAtUtc = now;

            dbContext.Audits.Add(CreateAudit(
                payment.ProjectId,
                "webhook.refund.succeeded",
                "billing-webhook",
                "refund succeeded webhook processed"));
            break;

        default:
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                $"Неподдерживаемый eventType: {eventType}.");
    }

    dbContext.WebhookEvents.Add(new BillingWebhookEventEntity
    {
        Id = Guid.NewGuid(),
        EventId = eventId,
        EventType = eventType,
        ProjectId = effectiveProjectId,
        PaymentId = payment?.Id,
        PayloadJson = JsonSerializer.Serialize(request),
        CreatedAtUtc = now,
        ProcessedAtUtc = now,
    });

    if (shouldRecalculateEntitlement)
    {
        await entitlementClient.RecalculateAsync(
            effectiveProjectId.Value,
            entitlementStatus,
            entitlementPlanKey,
            entitlementSource,
            $"billing:webhook:{eventId}",
            cancellationToken);
    }

    try
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException)
    {
        duplicate = await dbContext.WebhookEvents
            .AsNoTracking()
            .AnyAsync(x => x.EventId == eventId, cancellationToken);

        if (!duplicate)
        {
            throw;
        }
    }

    return Results.Ok(new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
});

app.MapPost("/internal/v1/payments/refund", async (
    HttpContext httpContext,
    Dictionary<string, JsonElement> request,
    BillingDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    var paymentId = ReadGuid(request, "paymentId");
    var reason = ReadOptionalString(request, "reason") ?? "manual-refund";

    return await idempotency.ExecuteAsync(
        dbContext,
        $"billing:payments:refund:{paymentId}",
        idempotencyKey,
        async ct =>
        {
            var payment = await dbContext.Payments.SingleOrDefaultAsync(x => x.Id == paymentId, ct);
            if (payment is null)
            {
                throw new ApiErrorException(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, "Платеж не найден.");
            }

            var requestedAmount = ReadDecimal(request, "amount", required: false) ?? payment.Amount;
            if (requestedAmount <= 0 || requestedAmount > payment.Amount)
            {
                throw new ApiErrorException(
                    StatusCodes.Status400BadRequest,
                    ApiErrorCodes.ValidationError,
                    "amount для refund должен быть больше нуля и не превышать сумму платежа.");
            }

            dbContext.Refunds.Add(new BillingRefundEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = payment.ProjectId,
                PaymentId = payment.Id,
                Amount = decimal.Round(requestedAmount, 2),
                Status = "accepted",
                Reason = reason,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });

            payment.Status = "refunded";
            payment.UpdatedAtUtc = DateTimeOffset.UtcNow;

            dbContext.Audits.Add(CreateAudit(
                payment.ProjectId,
                "payments.refund",
                "billing-api",
                reason));

            await dbContext.SaveChangesAsync(ct);
            return new IdempotentExecutionResult(StatusCodes.Status200OK, new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
        },
        cancellationToken);
});

app.MapPost("/internal/v1/subscriptions/manual-activate", async (
    HttpContext httpContext,
    Dictionary<string, JsonElement> request,
    BillingDbContext dbContext,
    IEntitlementClient entitlementClient,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    var projectId = ReadGuid(request, "projectId");
    var actor = ReadOptionalString(request, "actor") ?? "billing-operator";
    var reason = ReadOptionalString(request, "reason") ?? "manual-activate";
    var planKey = ReadOptionalString(request, "planKey") ?? "manual-basic";

    return await idempotency.ExecuteAsync(
        dbContext,
        $"billing:subscriptions:manual-activate:{projectId}",
        idempotencyKey,
        async ct =>
        {
            var now = DateTimeOffset.UtcNow;
            await UpsertSubscriptionAsync(
                dbContext,
                projectId,
                status: "active",
                planKey: planKey,
                currentPeriodEndsAtUtc: now.AddDays(30),
                graceEndsAtUtc: null,
                now,
                ct);

            await entitlementClient.RecalculateAsync(
                projectId,
                "active",
                planKey,
                "billing.manual-activate",
                idempotencyKey,
                ct);

            dbContext.Audits.Add(CreateAudit(projectId, "subscriptions.manual-activate", actor, reason));

            await dbContext.SaveChangesAsync(ct);
            return new IdempotentExecutionResult(StatusCodes.Status200OK, new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
        },
        cancellationToken);
});

app.MapPost("/internal/v1/subscriptions/reconcile", async (
    HttpContext httpContext,
    Dictionary<string, JsonElement> request,
    BillingDbContext dbContext,
    IEntitlementClient entitlementClient,
    CancellationToken cancellationToken) =>
{
    var projectId = TryReadGuid(request, "projectId");
    var now = DateTimeOffset.UtcNow;

    var query = dbContext.Subscriptions.AsQueryable();
    if (projectId is not null)
    {
        query = query.Where(x => x.ProjectId == projectId.Value);
    }

    var subscriptions = await query.ToListAsync(cancellationToken);
    var changed = 0;
    var changedSubscriptions = new List<BillingSubscriptionEntity>();

    foreach (var subscription in subscriptions)
    {
        if (subscription.Status == "grace" && subscription.GraceEndsAtUtc is not null && subscription.GraceEndsAtUtc <= now)
        {
            subscription.Status = "blocked";
            subscription.UpdatedAtUtc = now;
            changed++;
            changedSubscriptions.Add(subscription);

            dbContext.Audits.Add(CreateAudit(
                subscription.ProjectId,
                "subscriptions.reconcile",
                "billing-reconcile",
                "grace expired -> blocked"));
        }
    }

    if (changed > 0)
    {
        foreach (var subscription in changedSubscriptions)
        {
            await entitlementClient.RecalculateAsync(
                subscription.ProjectId,
                "blocked",
                subscription.PlanKey,
                "billing.reconcile",
                $"billing:reconcile:{subscription.ProjectId}:{now.ToUnixTimeSeconds()}",
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    return Results.Json(
        new AckResponse(httpContext.GetOrCreateRequestId(), "accepted"),
        statusCode: StatusCodes.Status202Accepted);
});

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

    var raw = value.GetString();
    return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
}

static decimal? ReadDecimal(Dictionary<string, JsonElement> payload, string key, bool required)
{
    if (!payload.TryGetValue(key, out var value))
    {
        if (required)
        {
            throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, $"Поле {key} обязательно.");
        }

        return null;
    }

    if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var decimalValue))
    {
        return decimalValue;
    }

    if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out decimalValue))
    {
        return decimalValue;
    }

    throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, $"Поле {key} должно быть числом.");
}

static async Task UpsertSubscriptionAsync(
    BillingDbContext dbContext,
    Guid projectId,
    string status,
    string planKey,
    DateTimeOffset? currentPeriodEndsAtUtc,
    DateTimeOffset? graceEndsAtUtc,
    DateTimeOffset now,
    CancellationToken cancellationToken)
{
    var existing = await dbContext.Subscriptions.SingleOrDefaultAsync(x => x.ProjectId == projectId, cancellationToken);
    if (existing is null)
    {
        dbContext.Subscriptions.Add(new BillingSubscriptionEntity
        {
            ProjectId = projectId,
            Status = status,
            PlanKey = planKey,
            TrialEndsAtUtc = null,
            GraceEndsAtUtc = graceEndsAtUtc,
            CurrentPeriodEndsAtUtc = currentPeriodEndsAtUtc,
            UpdatedAtUtc = now,
        });
        return;
    }

    existing.Status = status;
    existing.PlanKey = planKey;
    existing.GraceEndsAtUtc = graceEndsAtUtc;
    existing.CurrentPeriodEndsAtUtc = currentPeriodEndsAtUtc;
    existing.UpdatedAtUtc = now;
}

static BillingAuditEntity CreateAudit(Guid projectId, string operation, string actor, string reason)
{
    return new BillingAuditEntity
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId,
        Operation = operation,
        Actor = actor,
        Reason = reason,
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };
}

public sealed record AckResponse(string RequestId, string Status);

public sealed record GenericObjectResponse(string RequestId, IDictionary<string, object?> Data);

public partial class Program;
