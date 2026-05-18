using System.Text.Json;
using DDCRM.Core.Api.Integrations;
using DDCRM.Core.Persistence;
using DDCRM.Core.Persistence.Entities;
using DDCRM.Shared.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DDCRM.Core.Api.Workflows;

public interface IWorkflowNodeExecutor
{
    string NodeType { get; }

    Task<WorkflowNodeExecutionResult> ExecuteAsync(
        WorkflowNodeExecutionRequest request,
        CancellationToken cancellationToken);
}

public sealed record WorkflowNodeExecutionRequest(
    WorkflowNodeModel Node,
    WorkflowExecutionRuntimeContext Context,
    CoreDbContext DbContext);

public sealed class WorkflowNodeExecutorRegistry(IEnumerable<IWorkflowNodeExecutor> executors)
{
    private readonly Dictionary<string, IWorkflowNodeExecutor> _map =
        executors.ToDictionary(x => x.NodeType, StringComparer.Ordinal);

    public IWorkflowNodeExecutor Resolve(string nodeType)
    {
        if (_map.TryGetValue(nodeType, out var executor))
        {
            return executor;
        }

        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            $"Workflow node type `{nodeType}` не поддерживается.");
    }
}

public sealed record WorkflowOfferVariantValue(
    Guid OfferVariantId,
    Guid AccountId,
    string WorkerProductId,
    string Platform,
    decimal ObservedPrice,
    string ObservedCurrency,
    int Priority,
    bool IsActive);

public sealed class PurchaseStartNodeExecutor : IWorkflowNodeExecutor
{
    public string NodeType => WorkflowNodeTypes.PurchaseStart;

    public Task<WorkflowNodeExecutionResult> ExecuteAsync(
        WorkflowNodeExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var buyerId = request.Context.Variables.TryGetValue("buyerId", out var rawBuyerId)
            ? rawBuyerId?.ToString()
            : null;
        var payload = request.Context.Variables.TryGetValue("payload", out var rawPayload)
            ? rawPayload
            : null;
        var triggerSource = request.Context.Variables.TryGetValue("triggerSource", out var rawTriggerSource)
            ? rawTriggerSource?.ToString()
            : null;

        return Task.FromResult(
            new WorkflowNodeExecutionResult(
                new Dictionary<string, object?>
                {
                    ["event.type"] = "purchase",
                    ["event.platform"] = ReadRuntimeValueAsString(request.Context.Variables, "event.platform"),
                    ["event.quantity"] = ReadRuntimeValueAsNumber(request.Context.Variables, "event.quantity"),
                    ["event.amount"] = ReadRuntimeValueAsNumber(request.Context.Variables, "event.amount"),
                    ["event.currency"] = ReadRuntimeValueAsString(request.Context.Variables, "event.currency"),
                    ["purchase.projectId"] = request.Context.ProjectId.ToString(),
                    ["purchase.offerId"] = request.Context.OfferId.ToString(),
                    ["purchase.sourceOrderId"] = request.Context.SourceOrderId,
                    ["purchase.buyerId"] = buyerId,
                    ["purchase.payload"] = payload,
                    ["purchase.triggerSource"] = triggerSource,
                }));
    }

    private static string? ReadRuntimeValueAsString(IReadOnlyDictionary<string, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string asString => asString,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => value.ToString(),
        };
    }

    private static decimal? ReadRuntimeValueAsNumber(IReadOnlyDictionary<string, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int intValue => intValue,
            long longValue => longValue,
            float floatValue => Convert.ToDecimal(floatValue),
            double doubleValue => Convert.ToDecimal(doubleValue),
            decimal decimalValue => decimalValue,
            JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var parsedDecimal) => parsedDecimal,
            string asString when decimal.TryParse(asString, out var parsed) => parsed,
            _ => null,
        };
    }
}

public sealed class MessageStartNodeExecutor : IWorkflowNodeExecutor
{
    public string NodeType => WorkflowNodeTypes.MessageStart;

    public Task<WorkflowNodeExecutionResult> ExecuteAsync(
        WorkflowNodeExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var messageText = ReadRuntimeString(request.Context.Variables, "event.messageText");
        var platform = ReadRuntimeString(request.Context.Variables, "event.platform");
        var quantity = ReadRuntimeDecimal(request.Context.Variables, "event.quantity");
        var amount = ReadRuntimeDecimal(request.Context.Variables, "event.amount");
        var currency = ReadRuntimeString(request.Context.Variables, "event.currency");
        var buyerId = ReadRuntimeString(request.Context.Variables, "buyerId");
        var payload = request.Context.Variables.TryGetValue("payload", out var rawPayload) ? rawPayload : null;
        var chatId = ReadPayloadString(payload, "chatId", "telegramChatId");
        var conversationId = ReadPayloadString(payload, "conversationId");
        var accountId = ReadPayloadString(payload, "accountId");

        return Task.FromResult(
            new WorkflowNodeExecutionResult(
                new Dictionary<string, object?>
                {
                    ["event.type"] = "message",
                    ["event.platform"] = platform,
                    ["event.quantity"] = quantity,
                    ["event.amount"] = amount,
                    ["event.currency"] = currency,
                    ["message.text"] = messageText,
                    ["message.sourceOrderId"] = request.Context.SourceOrderId,
                    ["message.buyerId"] = buyerId,
                    ["message.chatId"] = chatId,
                    ["message.conversationId"] = conversationId,
                    ["message.accountId"] = accountId,
                    ["message.payload"] = payload,
                }));
    }

    private static string ReadRuntimeString(IReadOnlyDictionary<string, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var value) || value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            string asString => asString,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static decimal? ReadRuntimeDecimal(IReadOnlyDictionary<string, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int intValue => intValue,
            long longValue => longValue,
            float floatValue => Convert.ToDecimal(floatValue),
            double doubleValue => Convert.ToDecimal(doubleValue),
            decimal decimalValue => decimalValue,
            JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var parsed) => parsed,
            string asString when decimal.TryParse(asString, out var parsed) => parsed,
            _ => null,
        };
    }

    private static string? ReadPayloadString(object? payload, params string[] keys)
    {
        if (payload is null || keys.Length == 0)
        {
            return null;
        }

        if (payload is Dictionary<string, object?> dictionary)
        {
            foreach (var key in keys)
            {
                if (!dictionary.TryGetValue(key, out var rawValue) || rawValue is null)
                {
                    continue;
                }

                var value = rawValue.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        if (payload is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in keys)
            {
                if (!element.TryGetProperty(key, out var property))
                {
                    continue;
                }

                var value = property.ValueKind switch
                {
                    JsonValueKind.String => property.GetString(),
                    JsonValueKind.Number => property.GetRawText(),
                    _ => property.ToString(),
                };

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        return null;
    }
}

public sealed class ReviewStartNodeExecutor : IWorkflowNodeExecutor
{
    public string NodeType => WorkflowNodeTypes.ReviewStart;

    public Task<WorkflowNodeExecutionResult> ExecuteAsync(
        WorkflowNodeExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var reviewText = ReadRuntimeString(request.Context.Variables, "event.reviewText");
        var ratingRaw = ReadRuntimeString(request.Context.Variables, "event.reviewRating");
        var platform = ReadRuntimeString(request.Context.Variables, "event.platform");
        var quantity = ReadRuntimeDecimal(request.Context.Variables, "event.quantity");
        var amount = ReadRuntimeDecimal(request.Context.Variables, "event.amount");
        var currency = ReadRuntimeString(request.Context.Variables, "event.currency");
        var payload = request.Context.Variables.TryGetValue("payload", out var rawPayload) ? rawPayload : null;

        var rating = int.TryParse(ratingRaw, out var parsedRating) ? parsedRating : (int?)null;
        return Task.FromResult(
            new WorkflowNodeExecutionResult(
                new Dictionary<string, object?>
                {
                    ["event.type"] = "review",
                    ["event.platform"] = platform,
                    ["event.quantity"] = quantity,
                    ["event.amount"] = amount,
                    ["event.currency"] = currency,
                    ["review.text"] = reviewText,
                    ["review.rating"] = rating,
                    ["review.sourceOrderId"] = request.Context.SourceOrderId,
                    ["review.payload"] = payload,
                }));
    }

    private static string ReadRuntimeString(IReadOnlyDictionary<string, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var value) || value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            string asString => asString,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static decimal? ReadRuntimeDecimal(IReadOnlyDictionary<string, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int intValue => intValue,
            long longValue => longValue,
            float floatValue => Convert.ToDecimal(floatValue),
            double doubleValue => Convert.ToDecimal(doubleValue),
            decimal decimalValue => decimalValue,
            JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var parsed) => parsed,
            string asString when decimal.TryParse(asString, out var parsed) => parsed,
            _ => null,
        };
    }
}

public sealed class SetVariablesNodeExecutor : IWorkflowNodeExecutor
{
    public string NodeType => WorkflowNodeTypes.SetVariables;

    public Task<WorkflowNodeExecutionResult> ExecuteAsync(
        WorkflowNodeExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var values = ReadConfigObject(request.Node.Config, "values");
        var updates = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in values)
        {
            updates[item.Key] = JsonElementToRuntimeValue(item.Value);
        }

        return Task.FromResult(new WorkflowNodeExecutionResult(updates));
    }

    private static Dictionary<string, JsonElement> ReadConfigObject(Dictionary<string, JsonElement>? config, string key)
    {
        if (config is null
            || !config.TryGetValue(key, out var value)
            || value.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }

        return value.EnumerateObject()
            .ToDictionary(x => x.Name, x => x.Value.Clone(), StringComparer.Ordinal);
    }

    private static object? JsonElementToRuntimeValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var asLong) => asLong,
            JsonValueKind.Number when value.TryGetDecimal(out var asDecimal) => asDecimal,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object or JsonValueKind.Array => JsonSerializer.Deserialize<object?>(value.GetRawText()),
            _ => null,
        };
    }
}

public sealed class ConditionNodeExecutor : IWorkflowNodeExecutor
{
    public string NodeType => WorkflowNodeTypes.Condition;

    public Task<WorkflowNodeExecutionResult> ExecuteAsync(
        WorkflowNodeExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var field = ReadConfigString(request.Node.Config, "field");
        var expected = ReadConfigString(request.Node.Config, "equals");
        var actual = ReadRuntimeString(request.Context.Variables, field);
        var isMatch = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

        return Task.FromResult(
            new WorkflowNodeExecutionResult(
                new Dictionary<string, object?>
                {
                    [$"condition:{request.Node.Id}"] = isMatch,
                }));
    }

    private static string ReadRuntimeString(Dictionary<string, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var value) || value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            string asString => asString,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static string ReadConfigString(Dictionary<string, JsonElement>? config, string key)
    {
        if (config is null
            || !config.TryGetValue(key, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                $"Condition node config.{key} обязателен.");
        }

        return value.GetString()!.Trim();
    }
}

public sealed class LoadOfferNodeExecutor : IWorkflowNodeExecutor
{
    public string NodeType => WorkflowNodeTypes.LoadOffer;

    public async Task<WorkflowNodeExecutionResult> ExecuteAsync(
        WorkflowNodeExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var offer = await request.DbContext.Offers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == request.Context.OfferId && x.ProjectId == request.Context.ProjectId,
                cancellationToken);
        if (offer is null)
        {
            throw new ApiErrorException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound,
                "Offer для workflow не найден.");
        }

        var variants = await request.DbContext.OfferVariants
            .AsNoTracking()
            .Where(x => x.OfferId == offer.Id && x.ProjectId == request.Context.ProjectId && x.IsActive)
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.CreatedAtUtc)
            .Select(x => new WorkflowOfferVariantValue(
                x.Id,
                x.AccountId,
                x.WorkerProductId,
                x.Platform,
                x.ObservedPrice,
                x.ObservedCurrency,
                x.Priority,
                x.IsActive))
            .ToListAsync(cancellationToken);

        return new WorkflowNodeExecutionResult(
            new Dictionary<string, object?>
            {
                ["offer.id"] = offer.Id.ToString(),
                ["offer.name"] = offer.Name,
                ["offer.status"] = offer.Status,
                ["offerVariants"] = variants,
            });
    }
}

public sealed class SelectAccountPriorityFallbackNodeExecutor : IWorkflowNodeExecutor
{
    public string NodeType => WorkflowNodeTypes.SelectAccountPriorityFallback;

    public Task<WorkflowNodeExecutionResult> ExecuteAsync(
        WorkflowNodeExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.Context.Variables.TryGetValue("offerVariants", out var rawVariants)
            || rawVariants is not List<WorkflowOfferVariantValue> variants
            || variants.Count == 0)
        {
            throw new ApiErrorException(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.Conflict,
                "В Offer нет активных variants для выбора аккаунта.");
        }

        var preferredPlatform = ReadOptionalConfigString(request.Node.Config, "platform");
        var selected = variants
            .Where(x => string.IsNullOrWhiteSpace(preferredPlatform)
                        || string.Equals(x.Platform, preferredPlatform, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.OfferVariantId)
            .FirstOrDefault();

        selected ??= variants
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.OfferVariantId)
            .First();

        return Task.FromResult(
            new WorkflowNodeExecutionResult(
                new Dictionary<string, object?>
                {
                    ["selectedVariant.id"] = selected.OfferVariantId.ToString(),
                    ["selectedVariant.accountId"] = selected.AccountId.ToString(),
                    ["selectedVariant.workerProductId"] = selected.WorkerProductId,
                    ["selectedVariant.platform"] = selected.Platform,
                }));
    }

    private static string? ReadOptionalConfigString(Dictionary<string, JsonElement>? config, string key)
    {
        if (config is null || !config.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var trimmed = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

public sealed class InvokeWorkerActionNodeExecutor : IWorkflowNodeExecutor
{
    public string NodeType => WorkflowNodeTypes.InvokeWorkerAction;

    public Task<WorkflowNodeExecutionResult> ExecuteAsync(
        WorkflowNodeExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var action = ReadOptionalConfigString(request.Node.Config, "action") ?? "ext.integration.steam.jobs";
        return Task.FromResult(
            new WorkflowNodeExecutionResult(
                new Dictionary<string, object?>
                {
                    ["workerAction.status"] = "queued",
                    ["workerAction.action"] = action,
                }));
    }

    private static string? ReadOptionalConfigString(Dictionary<string, JsonElement>? config, string key)
    {
        if (config is null || !config.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}

public sealed class InvokeCustomHttpNodeExecutor(ICustomHttpIntegrationInvoker invoker) : IWorkflowNodeExecutor
{
    public string NodeType => WorkflowNodeTypes.InvokeCustomHttp;

    public async Task<WorkflowNodeExecutionResult> ExecuteAsync(
        WorkflowNodeExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var integrationId = ReadRequiredGuid(request.Node.Config, "integrationId");
        var method = ReadOptionalString(request.Node.Config, "method") ?? "POST";
        var relativePath = ReadOptionalString(request.Node.Config, "path");
        var bodyJson = ReadOptionalJsonBody(request.Node.Config, "payload");
        var headers = ReadOptionalHeaders(request.Node.Config, "headers");

        var invokeResult = await invoker.InvokeAsync(
            request.Context.ProjectId,
            integrationId,
            new CustomHttpInvokeRequest(method, relativePath, headers, bodyJson),
            cancellationToken);

        return new WorkflowNodeExecutionResult(
            new Dictionary<string, object?>
            {
                ["customHttp.statusCode"] = invokeResult.StatusCode,
                ["customHttp.endpoint"] = invokeResult.Endpoint,
                ["customHttp.body"] = invokeResult.Body,
            });
    }

    private static Guid ReadRequiredGuid(Dictionary<string, JsonElement>? config, string key)
    {
        var raw = ReadOptionalString(config, key);
        if (!Guid.TryParse(raw, out var parsed))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                $"InvokeCustomHttp node config.{key} должен быть GUID.");
        }

        return parsed;
    }

    private static string? ReadOptionalString(Dictionary<string, JsonElement>? config, string key)
    {
        if (config is null || !config.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? ReadOptionalJsonBody(Dictionary<string, JsonElement>? config, string key)
    {
        if (config is null || !config.TryGetValue(key, out var value))
        {
            return null;
        }

        return value.GetRawText();
    }

    private static IReadOnlyDictionary<string, string>? ReadOptionalHeaders(Dictionary<string, JsonElement>? config, string key)
    {
        if (config is null || !config.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                headers[property.Name] = property.Value.GetString()!.Trim();
            }
        }

        return headers;
    }
}

public sealed class SteamActionNodeExecutor(
    WorkflowWorkerBridgeClient workflowWorkerBridgeClient,
    IOptions<WorkflowMessagePollingOptions> workflowMessagePollingOptions)
    : IWorkflowNodeExecutor
{
    private static readonly HashSet<string> SteamReadOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "accounts.list",
        "jobs.list",
        "workflow.blocks.catalog",
        "workflow.actions.catalog",
        "rentals.availability.list",
        "rentals.account.select",
        "denuvo.availability.list",
        "denuvo.slot.stats",
    };

    private readonly WorkflowMessagePollingOptions _workflowMessagePollingOptions = workflowMessagePollingOptions.Value;

    public string NodeType => WorkflowNodeTypes.SteamAction;

    public async Task<WorkflowNodeExecutionResult> ExecuteAsync(
        WorkflowNodeExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var hasActiveSteamGrant = await request.DbContext.ProjectIntegrationGrants
            .AsNoTracking()
            .AnyAsync(
                x => x.ProjectId == request.Context.ProjectId
                     && x.IntegrationKey == IntegrationKeys.SteamAccountsManager
                     && x.Status == "active",
                cancellationToken);
        if (!hasActiveSteamGrant)
        {
            throw new ApiErrorException(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.Conflict,
                $"SteamAction node недоступен: интеграция `{IntegrationKeys.SteamAccountsManager}` не активна.");
        }

        var runtime = await request.DbContext.ProjectIntegrationWorkerRuntimes
            .AsNoTracking()
            .Where(x =>
                x.ProjectId == request.Context.ProjectId &&
                x.IntegrationKey == IntegrationKeys.SteamAccountsManager &&
                x.Status == "active")
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (runtime is null)
        {
            throw new ApiErrorException(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.Conflict,
                "SteamAction node недоступен: активный runtime интеграции Steam не найден.");
        }

        var operation = ReadRequiredStringTemplate(request.Node.Config, "action", request.Context.Variables);
        var workerAction = ResolveWorkerActionKey(request.Node.Config, operation, request.Context.Variables);
        var workerPayload = BuildWorkerPayload(request.Node.Config, request.Context, operation);

        var route = await workflowWorkerBridgeClient.ResolveRouteAsync(
            _workflowMessagePollingOptions,
            runtime.RuntimeAccountId,
            cancellationToken);
        if (route is null)
        {
            throw new InvalidOperationException(
                $"SteamAction: route не найден для runtime account `{runtime.RuntimeAccountId}`.");
        }

        var idempotencyKey = $"wf-steam-action:{request.Context.TriggerEventId:N}:{request.Node.Id.Trim()}";
        var responseElement = await workflowWorkerBridgeClient.InvokeActionAsync(
            _workflowMessagePollingOptions,
            route,
            workerAction,
            workerPayload,
            idempotencyKey,
            cancellationToken);

        var responseObject = JsonSerializer.Deserialize<object?>(responseElement.GetRawText());
        var variables = new Dictionary<string, object?>
        {
            ["steam.action.status"] = "completed",
            ["steam.action.type"] = operation,
            ["steam.action.integrationAction"] = workerAction,
            ["steam.action.runtimeAccountId"] = runtime.RuntimeAccountId.ToString(),
            ["steam.action.response"] = responseObject,
        };

        if (responseElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in responseElement.EnumerateObject())
            {
                variables[$"steam.action.{property.Name}"] =
                    ConvertJsonElement(property.Value);
            }
        }

        if (operation.StartsWith("rentals.", StringComparison.OrdinalIgnoreCase))
        {
            PromoteRentalVariables(responseElement, variables);
        }

        if (operation.StartsWith("denuvo.", StringComparison.OrdinalIgnoreCase))
        {
            PromoteDenuvoVariables(responseElement, variables);
        }

        return new WorkflowNodeExecutionResult(variables);
    }

    private static Dictionary<string, JsonElement> BuildWorkerPayload(
        Dictionary<string, JsonElement>? config,
        WorkflowExecutionRuntimeContext context,
        string operation)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        result["operation"] = operation;
        result["projectId"] = context.ProjectId;

        if (config is not null)
        {
            foreach (var pair in config)
            {
                if (pair.Key is "action" or "integrationAction" or "payload")
                {
                    continue;
                }

                result[pair.Key] = WorkflowRuntimeTemplateResolver.ResolveJsonElementTemplates(pair.Value, context.Variables);
            }

            if (config.TryGetValue("payload", out var payloadElement) &&
                payloadElement.ValueKind == JsonValueKind.Object)
            {
                var resolvedPayload = WorkflowRuntimeTemplateResolver.ResolveJsonElementTemplates(payloadElement, context.Variables);
                result["payload"] = resolvedPayload;

                if (resolvedPayload is IDictionary<string, object?> payloadDictionary &&
                    (operation.StartsWith("rentals.", StringComparison.OrdinalIgnoreCase)
                     || operation.StartsWith("denuvo.", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(operation, "workflow.blocks.enqueue", StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var entry in payloadDictionary)
                    {
                        if (!result.ContainsKey(entry.Key))
                        {
                            result[entry.Key] = entry.Value;
                        }
                    }
                }
            }
        }

        return result.ToDictionary(
            x => x.Key,
            x => JsonSerializer.SerializeToElement(x.Value),
            StringComparer.Ordinal);
    }

    private static void PromoteRentalVariables(
        JsonElement response,
        IDictionary<string, object?> target)
    {
        if (response.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in response.EnumerateObject())
        {
            target[$"rental.{property.Name}"] = ConvertJsonElement(property.Value);
        }

        if (!response.TryGetProperty("credentials", out var credentials) ||
            credentials.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in credentials.EnumerateObject())
        {
            target[$"rental.credentials.{property.Name}"] = ConvertJsonElement(property.Value);
            target[$"rental.{property.Name}"] = ConvertJsonElement(property.Value);
        }
    }

    private static void PromoteDenuvoVariables(
        JsonElement response,
        IDictionary<string, object?> target)
    {
        if (response.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in response.EnumerateObject())
        {
            target[$"denuvo.{property.Name}"] = ConvertJsonElement(property.Value);
        }
    }

    private static object? ConvertJsonElement(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var asLong) => asLong,
            JsonValueKind.Number when value.TryGetDecimal(out var asDecimal) => asDecimal,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object or JsonValueKind.Array => JsonSerializer.Deserialize<object?>(value.GetRawText()),
            _ => null,
        };
    }

    private static string ResolveWorkerActionKey(
        Dictionary<string, JsonElement>? config,
        string operation,
        IReadOnlyDictionary<string, object?> variables)
    {
        var raw = ReadOptionalStringTemplate(config, "integrationAction", variables);
        if (!string.IsNullOrWhiteSpace(raw))
        {
            return raw.Trim();
        }

        return SteamReadOperations.Contains(operation)
            ? "ext.integration.steam.read"
            : "ext.integration.steam.jobs";
    }

    private static string ReadRequiredStringTemplate(
        Dictionary<string, JsonElement>? config,
        string key,
        IReadOnlyDictionary<string, object?> variables)
    {
        var value = ReadOptionalStringTemplate(config, key, variables);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        throw new ApiErrorException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            $"SteamAction node config.{key} обязателен.");
    }

    private static string? ReadOptionalStringTemplate(
        Dictionary<string, JsonElement>? config,
        string key,
        IReadOnlyDictionary<string, object?> variables)
    {
        if (config is null || !config.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return value.GetRawText();
        }

        var rawText = value.GetString() ?? string.Empty;
        var resolved = WorkflowRuntimeTemplateResolver.ResolveStringTemplateValue(rawText, variables);
        return resolved?.ToString();
    }
}

public sealed class TaskNodeExecutor : IWorkflowNodeExecutor
{
    public string NodeType => WorkflowNodeTypes.Task;

    public Task<WorkflowNodeExecutionResult> ExecuteAsync(
        WorkflowNodeExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var taskType = ReadRequiredString(request.Node.Config, "taskType");
        var title = ReadOptionalString(request.Node.Config, "title");
        var assignee = ReadOptionalString(request.Node.Config, "assignee");
        var delaySeconds = ReadOptionalDelaySeconds(request.Node.Config);
        var payload = ReadOptionalPayload(request.Node.Config);

        var scheduledAtUtc = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
        return Task.FromResult(
            new WorkflowNodeExecutionResult(
                new Dictionary<string, object?>
                {
                    ["task.id"] = Guid.NewGuid().ToString(),
                    ["task.type"] = taskType,
                    ["task.title"] = title,
                    ["task.assignee"] = assignee,
                    ["task.delaySeconds"] = delaySeconds,
                    ["task.scheduledAtUtc"] = scheduledAtUtc.ToString("O"),
                    ["task.status"] = "scheduled",
                    ["task.payload"] = payload,
                }));
    }

    private static string ReadRequiredString(Dictionary<string, JsonElement>? config, string key)
    {
        if (config is null
            || !config.TryGetValue(key, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                $"Task node config.{key} обязателен.");
        }

        return value.GetString()!.Trim();
    }

    private static string? ReadOptionalString(Dictionary<string, JsonElement>? config, string key)
    {
        if (config is null || !config.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static int ReadOptionalDelaySeconds(Dictionary<string, JsonElement>? config)
    {
        if (config is null || !config.TryGetValue("delaySeconds", out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return Math.Max(0, number);
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
        {
            return Math.Max(0, parsed);
        }

        return 0;
    }

    private static object? ReadOptionalPayload(Dictionary<string, JsonElement>? config)
    {
        if (config is null || !config.TryGetValue("payload", out var value))
        {
            return null;
        }

        return JsonSerializer.Deserialize<object?>(value.GetRawText());
    }
}

public sealed class SendBuyerResponseNodeExecutor(
    TelegramNotificationSender telegramSender,
    ProjectSecretCrypto projectSecretCrypto,
    WorkflowWorkerBridgeClient workflowWorkerBridgeClient,
    IOptions<WorkflowMessagePollingOptions> workflowMessagePollingOptions,
    ILogger<SendBuyerResponseNodeExecutor> logger)
    : IWorkflowNodeExecutor
{
    private readonly WorkflowMessagePollingOptions _workflowMessagePollingOptions = workflowMessagePollingOptions.Value;

    public string NodeType => WorkflowNodeTypes.SendBuyerResponse;

    public async Task<WorkflowNodeExecutionResult> ExecuteAsync(
        WorkflowNodeExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var template = ReadOptionalString(request.Node.Config, "message")
            ?? "Спасибо за покупку. Данные по заказу подготовлены.";
        var message = WorkflowRuntimeTemplateResolver.RenderStringTemplate(template, request.Context.Variables);

        var dispatchChannel = await TryDispatchMessageAsync(
            request,
            message,
            cancellationToken);

        return new WorkflowNodeExecutionResult(
            new Dictionary<string, object?>
            {
                ["buyerResponse.message"] = message,
                ["buyerResponse.dispatchChannel"] = dispatchChannel ?? "none",
            });
    }

    private static string ReadRuntimeString(Dictionary<string, object?> source, string key)
    {
        var value = WorkflowRuntimeTemplateResolver.ResolvePathValue(key, source);
        if (value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonElement element when element.ValueKind == JsonValueKind.Number => element.GetRawText(),
            JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False => element.GetBoolean() ? "true" : "false",
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static string? ReadOptionalString(Dictionary<string, JsonElement>? config, string key)
    {
        if (config is null || !config.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private async Task<string?> TryDispatchMessageAsync(
        WorkflowNodeExecutionRequest request,
        string message,
        CancellationToken cancellationToken)
    {
        var platform = ReadRuntimeString(request.Context.Variables, "event.platform").Trim();
        var chatId = ReadRuntimeString(request.Context.Variables, "message.chatId").Trim();

        if (string.Equals(platform, "telegram", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(chatId))
        {
            var proxy = await ResolveActiveProxyAsync(request.DbContext, cancellationToken);
            var sendResult = await telegramSender.SendMessageWithDiagnosticsAsync(chatId, message, proxy, cancellationToken);
            if (!string.Equals(sendResult.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Отправка buyer-response в Telegram не удалась: {sendResult.ReasonCode ?? "unknown"}; {sendResult.ErrorMessage ?? "без деталей"}");
            }

            logger.LogInformation(
                "Workflow buyer response delivered. project={ProjectId} offer={OfferId} chatId={ChatId} path={Path}",
                request.Context.ProjectId,
                request.Context.OfferId,
                chatId,
                sendResult.EffectivePath ?? "unknown");

            return "telegram";
        }

        var accountIdRaw = ReadRuntimeString(request.Context.Variables, "message.accountId").Trim();
        var conversationId = ReadRuntimeString(request.Context.Variables, "message.conversationId").Trim();
        if (!Guid.TryParse(accountIdRaw, out var accountId) || string.IsNullOrWhiteSpace(conversationId))
        {
            return null;
        }

        if (!_workflowMessagePollingOptions.DispatchWorkerReplies)
        {
            logger.LogWarning(
                "Workflow worker reply dispatch skipped (test mode). project={ProjectId} offer={OfferId} account={AccountId} conversation={ConversationId}",
                request.Context.ProjectId,
                request.Context.OfferId,
                accountId,
                conversationId);
            return "worker-test-mode";
        }

        var route = await workflowWorkerBridgeClient.ResolveRouteAsync(_workflowMessagePollingOptions, accountId, cancellationToken);
        if (route is null)
        {
            throw new InvalidOperationException(
                $"Не удалось отправить buyer-response: route для account `{accountId}` не найден.");
        }

        var idempotencyKey = $"wf-send:{request.Context.TriggerEventId:N}:{request.Node.Id.Trim()}";
        var sent = await workflowWorkerBridgeClient.SendMessageAsync(
            _workflowMessagePollingOptions,
            route,
            conversationId,
            message,
            idempotencyKey,
            cancellationToken);
        if (!sent)
        {
            throw new InvalidOperationException(
                $"Не удалось отправить buyer-response через worker account `{accountId}` conversation `{conversationId}`.");
        }

        logger.LogInformation(
            "Workflow buyer response delivered via worker. project={ProjectId} offer={OfferId} account={AccountId} conversation={ConversationId}",
            request.Context.ProjectId,
            request.Context.OfferId,
            accountId,
            conversationId);

        return "worker";
    }

    private async Task<TelegramProxyRuntimeConfig?> ResolveActiveProxyAsync(
        CoreDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var proxy = await dbContext.TelegramProxyProfiles
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(x => x.IsActive, cancellationToken);

        if (proxy is null)
        {
            return null;
        }

        var scheme = string.Equals(proxy.ProxyType, "socks5", StringComparison.OrdinalIgnoreCase) ? "socks5" : "http";
        var uri = new Uri($"{scheme}://{proxy.Host}:{proxy.Port}");
        var login = string.IsNullOrWhiteSpace(proxy.LoginCiphertext) ? null : projectSecretCrypto.Decrypt(proxy.LoginCiphertext);
        var password = string.IsNullOrWhiteSpace(proxy.PasswordCiphertext) ? null : projectSecretCrypto.Decrypt(proxy.PasswordCiphertext);
        return new TelegramProxyRuntimeConfig(uri, login, password);
    }
}

public sealed class NotifyNodeExecutor : IWorkflowNodeExecutor
{
    public string NodeType => WorkflowNodeTypes.Notify;

    public Task<WorkflowNodeExecutionResult> ExecuteAsync(
        WorkflowNodeExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var message = ReadOptionalString(request.Node.Config, "message")
            ?? "workflow.notify";

        return Task.FromResult(
            new WorkflowNodeExecutionResult(
                new Dictionary<string, object?>
                {
                    ["notify.message"] = message,
                }));
    }

    private static string? ReadOptionalString(Dictionary<string, JsonElement>? config, string key)
    {
        if (config is null || !config.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}

public sealed class EndNodeExecutor : IWorkflowNodeExecutor
{
    public string NodeType => WorkflowNodeTypes.End;

    public Task<WorkflowNodeExecutionResult> ExecuteAsync(
        WorkflowNodeExecutionRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new WorkflowNodeExecutionResult(Stop: true));
    }
}
