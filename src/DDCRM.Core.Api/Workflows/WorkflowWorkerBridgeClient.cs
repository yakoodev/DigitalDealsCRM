using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DDCRM.Shared.Constants;

namespace DDCRM.Core.Api.Workflows;

public sealed class WorkflowWorkerBridgeClient(
    HttpClient httpClient,
    ILogger<WorkflowWorkerBridgeClient> logger)
{
    public async Task<WorkflowWorkerRouteBinding?> ResolveRouteAsync(
        WorkflowMessagePollingOptions options,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var routeKey = BuildRouteKey(accountId);
        var endpoint = BuildRouteRegistryEndpoint(options, "/internal/v1/routes/resolve-bulk");
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new Dictionary<string, object?>
            {
                ["routeKeys"] = new[] { routeKey },
            }),
        };
        AddHeaderIfPresent(request, HeaderNames.ServiceToken, options.InternalServiceToken);

        var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "Route resolve failed for account {AccountId}. status={StatusCode}; body={Body}",
                accountId,
                (int)response.StatusCode,
                TrimForLog(body));
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("routeKey", out var routeKeyProperty)
                || !string.Equals(routeKeyProperty.GetString(), routeKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!item.TryGetProperty("workerBinding", out var workerBinding) || workerBinding.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var workerId = ReadString(workerBinding, "workerId");
            var serverId = ReadString(workerBinding, "serverId");
            var podId = ReadString(workerBinding, "podId");

            if (string.IsNullOrWhiteSpace(workerId) || string.IsNullOrWhiteSpace(serverId))
            {
                continue;
            }

            return new WorkflowWorkerRouteBinding(accountId, routeKey, serverId!, workerId!, podId);
        }

        return null;
    }

    public async Task<IReadOnlyList<WorkflowConversationSummary>> ListConversationsAsync(
        WorkflowMessagePollingOptions options,
        WorkflowWorkerRouteBinding binding,
        int limit,
        bool onlyUnread,
        CancellationToken cancellationToken)
    {
        var query = $"limit={Math.Clamp(limit, 1, 100)}";
        if (onlyUnread)
        {
            query += "&onlyUnread=true";
        }

        var path = $"{BuildWorkerPathPrefix(options)}/conversations?{query}";
        var endpoint = BuildWorkerEndpoint(options, binding, path);
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        AddHeaderIfPresent(request, HeaderNames.ServiceToken, options.WorkerServiceToken);

        var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "conversations.list failed for account {AccountId}. status={StatusCode}; body={Body}",
                binding.AccountId,
                (int)response.StatusCode,
                TrimForLog(body));
            return [];
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<WorkflowConversationSummary>();
        foreach (var item in items.EnumerateArray())
        {
            var conversationId = ReadString(item, "conversationId");
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                continue;
            }

            result.Add(new WorkflowConversationSummary(
                conversationId!,
                ReadString(item, "peerId"),
                ReadString(item, "peerName")));
        }

        return result;
    }

    public async Task<IReadOnlyList<WorkflowConversationMessage>> ListMessagesAsync(
        WorkflowMessagePollingOptions options,
        WorkflowWorkerRouteBinding binding,
        string conversationId,
        int limit,
        CancellationToken cancellationToken)
    {
        var escapedConversationId = Uri.EscapeDataString(conversationId.Trim());
        var path = $"{BuildWorkerPathPrefix(options)}/conversations/{escapedConversationId}/messages?limit={Math.Clamp(limit, 1, 100)}";
        var endpoint = BuildWorkerEndpoint(options, binding, path);
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        AddHeaderIfPresent(request, HeaderNames.ServiceToken, options.WorkerServiceToken);

        var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "conversations.messages.list failed for account {AccountId}, conversation {ConversationId}. status={StatusCode}; body={Body}",
                binding.AccountId,
                conversationId,
                (int)response.StatusCode,
                TrimForLog(body));
            return [];
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<WorkflowConversationMessage>();
        foreach (var item in items.EnumerateArray())
        {
            var messageId = ReadString(item, "messageId");
            var direction = ReadString(item, "direction");
            var text = ReadString(item, "text");
            var createdAt = ReadDateTimeOffset(item, "createdAt");

            if (string.IsNullOrWhiteSpace(messageId))
            {
                continue;
            }

            result.Add(new WorkflowConversationMessage(
                messageId!,
                direction ?? string.Empty,
                text ?? string.Empty,
                createdAt));
        }

        return result;
    }

    public async Task<bool> SendMessageAsync(
        WorkflowMessagePollingOptions options,
        WorkflowWorkerRouteBinding binding,
        string conversationId,
        string text,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var escapedConversationId = Uri.EscapeDataString(conversationId.Trim());
        var path = $"{BuildWorkerPathPrefix(options)}/conversations/{escapedConversationId}/messages";
        var endpoint = BuildWorkerEndpoint(options, binding, path);
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new Dictionary<string, object?>
            {
                ["text"] = text,
                ["attachments"] = Array.Empty<object>(),
            }),
        };
        AddHeaderIfPresent(request, HeaderNames.ServiceToken, options.WorkerServiceToken);
        request.Headers.TryAddWithoutValidation(HeaderNames.IdempotencyKey, idempotencyKey);

        var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogWarning(
            "conversations.messages.send failed for account {AccountId}, conversation {ConversationId}. status={StatusCode}; body={Body}",
            binding.AccountId,
            conversationId,
            (int)response.StatusCode,
            TrimForLog(body));
        return false;
    }

    public async Task<JsonElement> InvokeActionAsync(
        WorkflowMessagePollingOptions options,
        WorkflowWorkerRouteBinding binding,
        string action,
        IDictionary<string, JsonElement> payload,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actionKey = action.Trim();
        if (actionKey.Length == 0)
        {
            throw new InvalidOperationException("Worker action key is empty.");
        }

        var path = $"{BuildWorkerPathPrefix(options)}/actions/{Uri.EscapeDataString(actionKey)}";
        var endpoint = BuildWorkerEndpoint(options, binding, path);
        var requestBody = new Dictionary<string, object?>
        {
            ["payload"] = payload,
        };

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(requestBody),
        };
        AddHeaderIfPresent(request, HeaderNames.ServiceToken, options.WorkerServiceToken);
        request.Headers.TryAddWithoutValidation(HeaderNames.IdempotencyKey, idempotencyKey);

        var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "worker.actions.invoke failed for account {AccountId}, action {Action}. status={StatusCode}; body={Body}",
                binding.AccountId,
                actionKey,
                (int)response.StatusCode,
                TrimForLog(body));
            throw new InvalidOperationException(
                $"Worker action `{actionKey}` failed with status {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static string BuildRouteRegistryEndpoint(WorkflowMessagePollingOptions options, string path)
    {
        var baseUrl = options.RouteRegistryBaseUrl.TrimEnd('/');
        return $"{baseUrl}{path}";
    }

    private static string BuildWorkerEndpoint(WorkflowMessagePollingOptions options, WorkflowWorkerRouteBinding binding, string path)
    {
        var baseUrl = options.WorkerBaseUrlTemplate
            .Replace("{serverId}", binding.ServerId, StringComparison.OrdinalIgnoreCase)
            .Replace("{workerId}", binding.WorkerId, StringComparison.OrdinalIgnoreCase)
            .Replace("{podId}", binding.PodId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');
        return $"{baseUrl}{path}";
    }

    private static string BuildWorkerPathPrefix(WorkflowMessagePollingOptions options)
    {
        var prefix = options.WorkerPathPrefix.Trim();
        if (prefix.Length == 0)
        {
            return "/internal/v2/worker";
        }

        return prefix.StartsWith("/", StringComparison.Ordinal) ? prefix.TrimEnd('/') : $"/{prefix.TrimEnd('/')}";
    }

    private static void AddHeaderIfPresent(HttpRequestMessage request, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }

    private static string BuildRouteKey(Guid accountId) => $"rk.{accountId:N}";

    private static string? ReadString(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement source, string propertyName)
    {
        var raw = ReadString(source, propertyName);
        return DateTimeOffset.TryParse(raw, out var parsed) ? parsed : null;
    }

    private static string TrimForLog(string value)
    {
        if (value.Length <= 1000)
        {
            return value;
        }

        var builder = new StringBuilder(value[..1000]);
        builder.Append("...");
        return builder.ToString();
    }
}

public sealed record WorkflowWorkerRouteBinding(
    Guid AccountId,
    string RouteKey,
    string ServerId,
    string WorkerId,
    string? PodId);

public sealed record WorkflowConversationSummary(
    string ConversationId,
    string? PeerId,
    string? PeerName);

public sealed record WorkflowConversationMessage(
    string MessageId,
    string Direction,
    string Text,
    DateTimeOffset? CreatedAtUtc);
