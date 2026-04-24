using System.Text.Json;
using DDCRM.Entitlement.Persistence;
using DDCRM.Entitlement.Persistence.Entities;
using DDCRM.Shared.Auth;
using DDCRM.Shared.Errors;
using DDCRM.Shared.Extensions;
using DDCRM.Shared.Idempotency;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<EntitlementDbContext>((serviceProvider, options) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var useInMemoryDb = configuration.GetValue("TEST_USE_INMEMORY_DB", false);

    if (useInMemoryDb)
    {
        options.UseInMemoryDatabase(configuration["TEST_INMEMORY_DB_NAME"] ?? "ddcrm-entitlement-tests");
        return;
    }

    options.UseNpgsql(
        configuration.GetConnectionString("EntitlementDb")
        ?? configuration["ENTITLEMENT_DB_CONNECTION"]
        ?? "Host=localhost;Port=5432;Database=ddcrm_entitlement;Username=postgres;Password=postgres");
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
    var dbContext = scope.ServiceProvider.GetRequiredService<EntitlementDbContext>();
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

app.MapPost("/internal/v1/entitlement/check", async (
    HttpContext httpContext,
    Dictionary<string, JsonElement> request,
    EntitlementDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var projectId = ReadGuid(request, "projectId");
    var action = ReadOptionalString(request, "action") ?? string.Empty;
    var now = DateTimeOffset.UtcNow;

    var snapshot = await dbContext.Snapshots
        .AsNoTracking()
        .SingleOrDefaultAsync(x => x.ProjectId == projectId, cancellationToken);

    var state = snapshot?.State ?? "active";
    var snapshotData = DeserializeData(snapshot?.DataJson);

    var blockedActions = ReadStringSet(snapshotData, "blockedActions");
    var allowedActions = ReadStringSet(snapshotData, "allowedActions");

    var overrides = await dbContext.Overrides
        .AsNoTracking()
        .Where(x => x.ProjectId == projectId && x.Status == "active" && x.ExpiresAtUtc > now)
        .OrderBy(x => x.CreatedAtUtc)
        .ToListAsync(cancellationToken);

    var allowed = !string.Equals(state, "blocked", StringComparison.OrdinalIgnoreCase);

    if (blockedActions.Contains("*") || (action.Length > 0 && blockedActions.Contains(action)))
    {
        allowed = false;
    }

    if (allowedActions.Contains("*") || (action.Length > 0 && allowedActions.Contains(action)))
    {
        allowed = true;
    }

    foreach (var current in overrides)
    {
        var data = DeserializeData(current.DataJson);
        var forceBlock = ReadBool(data, "forceBlock");
        var forceAllow = ReadBool(data, "forceAllow");

        var overrideBlockedActions = ReadStringSet(data, "blockedActions");
        var overrideAllowedActions = ReadStringSet(data, "allowedActions");

        if (forceBlock)
        {
            allowed = false;
        }

        if (overrideBlockedActions.Contains("*") || (action.Length > 0 && overrideBlockedActions.Contains(action)))
        {
            allowed = false;
        }

        if (forceAllow)
        {
            allowed = true;
        }

        if (overrideAllowedActions.Contains("*") || (action.Length > 0 && overrideAllowedActions.Contains(action)))
        {
            allowed = true;
        }
    }

    var response = new Dictionary<string, object?>
    {
        ["projectId"] = projectId,
        ["action"] = action,
        ["allowed"] = allowed,
        ["state"] = state,
        ["activeOverrides"] = overrides.Count,
    };

    return Results.Ok(new GenericObjectResponse(httpContext.GetOrCreateRequestId(), response));
});

app.MapPost("/internal/v1/entitlement/recalculate", async (
    HttpContext httpContext,
    Dictionary<string, JsonElement> request,
    EntitlementDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    var projectId = ReadGuid(request, "projectId");
    var subscriptionStatus = ReadOptionalString(request, "subscriptionStatus") ?? "active";
    var planKey = ReadOptionalString(request, "planKey") ?? "basic";

    var state = ResolveState(subscriptionStatus);
    var blockedActions = ReadStringArray(request, "blockedActions");
    var allowedActions = ReadStringArray(request, "allowedActions");

    if (blockedActions.Count == 0 && string.Equals(state, "blocked", StringComparison.OrdinalIgnoreCase))
    {
        blockedActions.Add("*");
    }

    var limits = ReadObject(request, "limits");
    var addons = ReadStringArray(request, "addons");

    return await idempotency.ExecuteAsync(
        dbContext,
        $"entitlement:recalculate:{projectId}",
        idempotencyKey,
        async ct =>
        {
            var now = DateTimeOffset.UtcNow;
            var data = new Dictionary<string, object?>
            {
                ["projectId"] = projectId,
                ["state"] = state,
                ["subscriptionStatus"] = subscriptionStatus,
                ["planKey"] = planKey,
                ["blockedActions"] = blockedActions,
                ["allowedActions"] = allowedActions,
                ["limits"] = limits,
                ["addons"] = addons,
                ["recalculatedAt"] = now,
            };

            var snapshot = await dbContext.Snapshots.SingleOrDefaultAsync(x => x.ProjectId == projectId, ct);
            var dataJson = JsonSerializer.Serialize(data, CreateJsonOptions());

            if (snapshot is null)
            {
                dbContext.Snapshots.Add(new EntitlementSnapshotEntity
                {
                    ProjectId = projectId,
                    State = state,
                    DataJson = dataJson,
                    CalculatedAtUtc = now,
                    UpdatedAtUtc = now,
                });
            }
            else
            {
                snapshot.State = state;
                snapshot.DataJson = dataJson;
                snapshot.CalculatedAtUtc = now;
                snapshot.UpdatedAtUtc = now;
            }

            dbContext.Audits.Add(CreateAudit(projectId, "recalculate", "entitlement-api", $"state={state};plan={planKey}"));
            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(StatusCodes.Status202Accepted, new AckResponse(httpContext.GetOrCreateRequestId(), "accepted"));
        },
        cancellationToken);
});

app.MapPost("/internal/v1/entitlement/override", async (
    HttpContext httpContext,
    Dictionary<string, JsonElement> request,
    EntitlementDbContext dbContext,
    IdempotencyExecutor idempotency,
    CancellationToken cancellationToken) =>
{
    var idempotencyKey = httpContext.RequireIdempotencyKey();
    var projectId = ReadGuid(request, "projectId");
    var reason = ReadRequiredString(request, "reason", minLength: 3);
    var actor = ReadRequiredString(request, "actor", minLength: 2);
    var expiresAtUtc = ReadDateTimeOffset(request, "expiresAt");

    if (expiresAtUtc <= DateTimeOffset.UtcNow)
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "expiresAt должен быть в будущем.");
    }

    var forceAllow = ReadOptionalBool(request, "forceAllow");
    var forceBlock = ReadOptionalBool(request, "forceBlock");
    var blockedActions = ReadStringArray(request, "blockedActions");
    var allowedActions = ReadStringArray(request, "allowedActions");
    var stateOverride = ReadOptionalString(request, "state");

    if (stateOverride is not null && !IsAllowedState(stateOverride))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, "Некорректное значение state для override.");
    }

    return await idempotency.ExecuteAsync(
        dbContext,
        $"entitlement:override:{projectId}",
        idempotencyKey,
        async ct =>
        {
            var now = DateTimeOffset.UtcNow;
            var payload = new Dictionary<string, object?>
            {
                ["projectId"] = projectId,
                ["reason"] = reason,
                ["actor"] = actor,
                ["expiresAt"] = expiresAtUtc,
                ["forceAllow"] = forceAllow,
                ["forceBlock"] = forceBlock,
                ["blockedActions"] = blockedActions,
                ["allowedActions"] = allowedActions,
                ["state"] = stateOverride,
            };

            dbContext.Overrides.Add(new EntitlementOverrideEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Actor = actor,
                Reason = reason,
                Status = "active",
                ExpiresAtUtc = expiresAtUtc,
                CreatedAtUtc = now,
                DataJson = JsonSerializer.Serialize(payload, CreateJsonOptions()),
            });

            if (!string.IsNullOrWhiteSpace(stateOverride))
            {
                var snapshot = await dbContext.Snapshots.SingleOrDefaultAsync(x => x.ProjectId == projectId, ct);
                var data = snapshot is null
                    ? new Dictionary<string, object?>()
                    : DeserializeData(snapshot.DataJson);

                data["stateOverride"] = stateOverride;
                data["stateOverrideActor"] = actor;
                data["stateOverrideReason"] = reason;
                data["stateOverrideAt"] = now;

                if (snapshot is null)
                {
                    dbContext.Snapshots.Add(new EntitlementSnapshotEntity
                    {
                        ProjectId = projectId,
                        State = stateOverride,
                        DataJson = JsonSerializer.Serialize(data, CreateJsonOptions()),
                        CalculatedAtUtc = now,
                        UpdatedAtUtc = now,
                    });
                }
                else
                {
                    snapshot.State = stateOverride;
                    snapshot.DataJson = JsonSerializer.Serialize(data, CreateJsonOptions());
                    snapshot.UpdatedAtUtc = now;
                }
            }

            dbContext.Audits.Add(CreateAudit(projectId, "override", actor, reason));
            await dbContext.SaveChangesAsync(ct);

            return new IdempotentExecutionResult(StatusCodes.Status200OK, new AckResponse(httpContext.GetOrCreateRequestId(), "completed"));
        },
        cancellationToken);
});

app.MapGet("/internal/v1/entitlement/{projectId:guid}", async (
    HttpContext httpContext,
    Guid projectId,
    EntitlementDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var now = DateTimeOffset.UtcNow;

    var snapshot = await dbContext.Snapshots
        .AsNoTracking()
        .SingleOrDefaultAsync(x => x.ProjectId == projectId, cancellationToken);

    var activeOverrides = await dbContext.Overrides
        .AsNoTracking()
        .CountAsync(
            x => x.ProjectId == projectId && x.Status == "active" && x.ExpiresAtUtc > now,
            cancellationToken);

    var data = snapshot is null
        ? new Dictionary<string, object?>
        {
            ["projectId"] = projectId,
            ["state"] = "active",
            ["activeOverrides"] = activeOverrides,
            ["calculatedAt"] = null,
        }
        : DeserializeData(snapshot.DataJson);

    data["projectId"] = projectId;
    data["state"] = snapshot?.State ?? "active";
    data["activeOverrides"] = activeOverrides;
    data["calculatedAt"] = snapshot?.CalculatedAtUtc;

    return Results.Ok(new GenericObjectResponse(httpContext.GetOrCreateRequestId(), data));
});

app.Run();

return;

static EntitlementAuditEntity CreateAudit(Guid projectId, string operation, string actor, string details)
{
    return new EntitlementAuditEntity
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId,
        Operation = operation,
        Actor = actor,
        Details = details,
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };
}

static Guid ReadGuid(Dictionary<string, JsonElement> payload, string key)
{
    if (!payload.TryGetValue(key, out var value) || !Guid.TryParse(value.GetString(), out var parsed))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, $"Поле {key} должно быть GUID.");
    }

    return parsed;
}

static string ReadRequiredString(Dictionary<string, JsonElement> payload, string key, int minLength)
{
    var value = ReadOptionalString(payload, key);
    if (string.IsNullOrWhiteSpace(value) || value.Length < minLength)
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, $"Поле {key} обязательно и должно быть длиной не менее {minLength} символов.");
    }

    return value;
}

static string? ReadOptionalString(Dictionary<string, JsonElement> payload, string key)
{
    if (!payload.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.String)
    {
        return null;
    }

    var parsed = value.GetString();
    return string.IsNullOrWhiteSpace(parsed) ? null : parsed.Trim();
}

static DateTimeOffset ReadDateTimeOffset(Dictionary<string, JsonElement> payload, string key)
{
    if (!payload.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.String || !DateTimeOffset.TryParse(value.GetString(), out var parsed))
    {
        throw new ApiErrorException(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError, $"Поле {key} должно быть валидной датой/временем в ISO-формате.");
    }

    return parsed;
}

static bool? ReadOptionalBool(Dictionary<string, JsonElement> payload, string key)
{
    if (!payload.TryGetValue(key, out var value))
    {
        return null;
    }

    return value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}

static List<string> ReadStringArray(Dictionary<string, JsonElement> payload, string key)
{
    if (!payload.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Array)
    {
        return [];
    }

    return value.EnumerateArray()
        .Where(x => x.ValueKind == JsonValueKind.String)
        .Select(x => x.GetString())
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static Dictionary<string, object?> ReadObject(Dictionary<string, JsonElement> payload, string key)
{
    if (!payload.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Object)
    {
        return new Dictionary<string, object?>();
    }

    return JsonSerializer.Deserialize<Dictionary<string, object?>>(value.GetRawText(), CreateJsonOptions())
           ?? new Dictionary<string, object?>();
}

static string ResolveState(string subscriptionStatus)
{
    return subscriptionStatus.Trim().ToLowerInvariant() switch
    {
        "trial" => "trial",
        "active" => "active",
        "paid" => "active",
        "grace" => "grace",
        "blocked" => "blocked",
        "unpaid" => "blocked",
        "failed" => "blocked",
        "canceled" => "blocked",
        _ => "active",
    };
}

static Dictionary<string, object?> DeserializeData(string? json)
{
    if (string.IsNullOrWhiteSpace(json))
    {
        return new Dictionary<string, object?>();
    }

    return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, CreateJsonOptions())
           ?? new Dictionary<string, object?>();
}

static HashSet<string> ReadStringSet(Dictionary<string, object?> payload, string key)
{
    if (!payload.TryGetValue(key, out var value) || value is null)
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    if (value is JsonElement jsonElement)
    {
        if (jsonElement.ValueKind == JsonValueKind.String)
        {
            var stringValue = jsonElement.GetString();
            return string.IsNullOrWhiteSpace(stringValue)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(new[] { stringValue }, StringComparer.OrdinalIgnoreCase);
        }

        if (jsonElement.ValueKind != JsonValueKind.Array)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return jsonElement.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    if (value is string singleValue)
    {
        return new HashSet<string>(new[] { singleValue }, StringComparer.OrdinalIgnoreCase);
    }

    if (value is IEnumerable<object?> values)
    {
        return values
            .OfType<string>()
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

static bool ReadBool(Dictionary<string, object?> payload, string key)
{
    if (!payload.TryGetValue(key, out var value) || value is null)
    {
        return false;
    }

    if (value is bool boolValue)
    {
        return boolValue;
    }

    if (value is JsonElement jsonElement)
    {
        return jsonElement.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(jsonElement.GetString(), out var parsed) => parsed,
            _ => false,
        };
    }

    if (value is string stringValue && bool.TryParse(stringValue, out var parsedString))
    {
        return parsedString;
    }

    return false;
}

static bool IsAllowedState(string value)
{
    return value.Trim().ToLowerInvariant() is "trial" or "active" or "grace" or "blocked";
}

static JsonSerializerOptions CreateJsonOptions() => new(JsonSerializerDefaults.Web);

public sealed record AckResponse(string RequestId, string Status);

public sealed record GenericObjectResponse(string RequestId, IDictionary<string, object?> Data);

public partial class Program;
