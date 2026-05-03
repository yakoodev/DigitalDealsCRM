using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using DDCRM.Core.Persistence;
using DDCRM.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DDCRM.Core.Api.Integrations;

public sealed class TelegramBotPollingBackgroundService(
    IServiceProvider serviceProvider,
    IOptions<TelegramNotificationOptions> options,
    ILogger<TelegramBotPollingBackgroundService> logger)
    : BackgroundService
{
    private readonly TelegramNotificationOptions _options = options.Value;
    private long _nextOffset;
    private bool _backlogDrained;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.BotToken))
        {
            logger.LogInformation("Telegram bot polling disabled: notifications disabled or bot token missing.");
            return;
        }

        if (!_options.PollingEnabled)
        {
            logger.LogInformation("Telegram bot polling disabled by configuration.");
            return;
        }

        logger.LogInformation("Telegram bot polling started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAndProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Telegram polling iteration failed.");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.PollingIntervalSeconds)), stoppingToken);
            }
        }
    }

    private async Task PollAndProcessAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<ProjectSecretCrypto>();
        var sender = scope.ServiceProvider.GetRequiredService<TelegramNotificationSender>();

        var proxy = await ResolveActiveProxyAsync(dbContext, crypto, cancellationToken);
        var updates = await GetUpdatesAsync(proxy, cancellationToken);
        if (updates.Count == 0)
        {
            return;
        }

        var maxOffset = updates.Max(x => x.UpdateId) + 1;
        if (!_backlogDrained)
        {
            _backlogDrained = true;
            _nextOffset = Math.Max(_nextOffset, maxOffset);
            logger.LogInformation("Telegram polling backlog drained. skippedUpdates={Count}, nextOffset={Offset}", updates.Count, _nextOffset);
            return;
        }

        foreach (var update in updates.OrderBy(x => x.UpdateId))
        {
            _nextOffset = Math.Max(_nextOffset, update.UpdateId + 1);
            if (update.Message is null || string.IsNullOrWhiteSpace(update.Message.Text))
            {
                continue;
            }

            await HandleMessageAsync(dbContext, sender, update.UpdateId, update.Message, proxy, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task HandleMessageAsync(
        CoreDbContext dbContext,
        TelegramNotificationSender sender,
        long updateId,
        TelegramIncomingMessage message,
        TelegramProxyRuntimeConfig? proxy,
        CancellationToken cancellationToken)
    {
        var text = message.Text!.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (TryExtractStartLinkCode(text, out var startLinkCode))
        {
            var startReply = await LinkChatByCodeAsync(dbContext, startLinkCode!, message, cancellationToken);
            await sender.SendMessageWithDiagnosticsAsync(
                message.Chat.Id.ToString(),
                startReply,
                proxy,
                cancellationToken);
            return;
        }

        if (IsCommand(text, "start"))
        {
            await sender.SendMessageWithDiagnosticsAsync(
                message.Chat.Id.ToString(),
                "DDCRM bot online. Команды: /ping, /link <код>. Можно открыть deep-link с payload link_<код>.",
                proxy,
                cancellationToken);
            return;
        }

        if (IsCommand(text, "ping"))
        {
            await sender.SendMessageWithDiagnosticsAsync(
                message.Chat.Id.ToString(),
                "pong",
                proxy,
                cancellationToken);
            return;
        }

        if (TryExtractLinkCode(text, out var code))
        {
            var reply = await LinkChatByCodeAsync(dbContext, code!, message, cancellationToken);
            await sender.SendMessageWithDiagnosticsAsync(
                message.Chat.Id.ToString(),
                reply,
                proxy,
                cancellationToken);
            return;
        }

        await TryEnqueueWorkflowMessageTriggersAsync(
            dbContext,
            updateId,
            message,
            text,
            cancellationToken);
    }

    private static bool IsCommand(string text, string command)
    {
        if (!text.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        var commandPart = text.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(commandPart))
        {
            return false;
        }

        commandPart = commandPart[1..];
        var atIndex = commandPart.IndexOf('@');
        if (atIndex >= 0)
        {
            commandPart = commandPart[..atIndex];
        }

        return string.Equals(commandPart, command, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExtractLinkCode(string text, out string? code)
    {
        code = null;
        if (!text.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = text.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        var commandPart = parts[0];
        var atIndex = commandPart.IndexOf('@');
        if (atIndex >= 0)
        {
            commandPart = commandPart[..atIndex];
        }

        if (!string.Equals(commandPart, "/link", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        code = parts[1].Trim();
        return !string.IsNullOrWhiteSpace(code);
    }

    private static bool TryExtractStartLinkCode(string text, out string? code)
    {
        code = null;
        if (!text.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = text.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        var commandPart = parts[0];
        var atIndex = commandPart.IndexOf('@');
        if (atIndex >= 0)
        {
            commandPart = commandPart[..atIndex];
        }

        if (!string.Equals(commandPart, "/start", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var payload = parts[1].Trim();
        if (payload.StartsWith("link_", StringComparison.OrdinalIgnoreCase))
        {
            payload = payload[5..].Trim();
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        code = payload;
        return true;
    }

    private static async Task<string> LinkChatByCodeAsync(
        CoreDbContext dbContext,
        string code,
        TelegramIncomingMessage message,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var linkCode = await dbContext.TelegramLinkCodes.SingleOrDefaultAsync(x => x.Code == code, cancellationToken);
        if (linkCode is null || linkCode.ConsumedAtUtc.HasValue || linkCode.ExpiresAtUtc <= now)
        {
            return "Код не найден или истёк. Сгенерируйте новый код в DDCRM.";
        }

        var grant = await dbContext.ProjectIntegrationGrants.SingleOrDefaultAsync(
            x => x.ProjectId == linkCode.ProjectId
                 && x.IntegrationKey == IntegrationKeys.Telegram
                 && x.Status == "active",
            cancellationToken);
        if (grant is null)
        {
            return "Telegram интеграция для проекта отключена. Попросите админа выдать доступ.";
        }

        var chatId = message.Chat.Id.ToString();
        var chatTitle = message.Chat.Title;
        var chatType = (message.Chat.Type ?? string.Empty).Trim().ToLowerInvariant();

        if (string.Equals(linkCode.BindingType, "group", StringComparison.OrdinalIgnoreCase))
        {
            if (chatType is not ("group" or "supergroup" or "channel"))
            {
                return "Этот код предназначен для группового чата. Выполните /link <код> внутри группы.";
            }

            var existingGroup = await dbContext.TelegramChatBindings.SingleOrDefaultAsync(
                x => x.ProjectId == linkCode.ProjectId
                     && x.BindingType == "group"
                     && x.ChatId == chatId,
                cancellationToken);

            if (existingGroup is null)
            {
                dbContext.TelegramChatBindings.Add(new TelegramChatBindingEntity
                {
                    Id = Guid.NewGuid(),
                    ProjectId = linkCode.ProjectId,
                    ChatId = chatId,
                    BindingType = "group",
                    UserId = null,
                    ChatTitle = chatTitle,
                    LinkedByUserId = linkCode.CreatedByUserId,
                    LinkedAtUtc = now,
                });
            }
            else
            {
                existingGroup.ChatTitle = chatTitle;
                existingGroup.LinkedAtUtc = now;
            }
        }
        else
        {
            var existingUser = await dbContext.TelegramChatBindings.SingleOrDefaultAsync(
                x => x.ProjectId == linkCode.ProjectId
                     && x.BindingType == "user"
                     && x.UserId == linkCode.CreatedByUserId,
                cancellationToken);

            if (existingUser is null)
            {
                dbContext.TelegramChatBindings.Add(new TelegramChatBindingEntity
                {
                    Id = Guid.NewGuid(),
                    ProjectId = linkCode.ProjectId,
                    ChatId = chatId,
                    BindingType = "user",
                    UserId = linkCode.CreatedByUserId,
                    ChatTitle = chatTitle,
                    LinkedByUserId = linkCode.CreatedByUserId,
                    LinkedAtUtc = now,
                });
            }
            else
            {
                existingUser.ChatId = chatId;
                existingUser.ChatTitle = chatTitle;
                existingUser.LinkedAtUtc = now;
            }
        }

        linkCode.ConsumedAtUtc = now;
        return $"Чат привязан к проекту {linkCode.ProjectId}.";
    }

    private async Task TryEnqueueWorkflowMessageTriggersAsync(
        CoreDbContext dbContext,
        long updateId,
        TelegramIncomingMessage message,
        string messageText,
        CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id.ToString(CultureInfo.InvariantCulture);
        var projectIds = await dbContext.TelegramChatBindings
            .AsNoTracking()
            .Where(x => x.ChatId == chatId)
            .Select(x => x.ProjectId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (projectIds.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var projectId in projectIds)
        {
            var targetOfferId = await ResolveMessageWorkflowOfferIdAsync(dbContext, projectId, cancellationToken);
            if (!targetOfferId.HasValue)
            {
                continue;
            }

            var sourceOrderId = BuildWorkflowSourceOrderId(chatId, updateId);
            var duplicate = await dbContext.WorkflowTriggerEvents.AnyAsync(
                x => x.ProjectId == projectId
                     && x.Source == "message-telegram-polling"
                     && x.SourceOrderId == sourceOrderId,
                cancellationToken);
            if (duplicate)
            {
                continue;
            }

            var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["platform"] = "telegram",
                ["messageText"] = messageText,
                ["chatId"] = chatId,
                ["telegramUpdateId"] = updateId,
                ["chatType"] = message.Chat.Type,
                ["chatTitle"] = message.Chat.Title,
            };

            var triggerEvent = new WorkflowTriggerEventEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                OfferId = targetOfferId.Value,
                Source = "message-telegram-polling",
                SourceOrderId = sourceOrderId,
                BuyerId = chatId,
                PayloadJson = JsonSerializer.Serialize(payload),
                Status = "accepted",
                CreatedAtUtc = now,
            };

            dbContext.WorkflowTriggerEvents.Add(triggerEvent);
            dbContext.WorkflowOutbox.Add(new WorkflowOutboxEntity
            {
                Id = Guid.NewGuid(),
                TriggerEventId = triggerEvent.Id,
                ProjectId = projectId,
                OfferId = targetOfferId.Value,
                Status = "pending",
                AttemptCount = 0,
                NextAttemptAtUtc = now,
                CreatedAtUtc = now,
            });

            logger.LogInformation(
                "Telegram message workflow trigger enqueued. project={ProjectId} offer={OfferId} sourceOrderId={SourceOrderId}",
                projectId,
                targetOfferId.Value,
                sourceOrderId);
        }
    }

    private async Task<Guid?> ResolveMessageWorkflowOfferIdAsync(
        CoreDbContext dbContext,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var definitions = await dbContext.WorkflowDefinitions
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.PublishedJson != null)
            .OrderByDescending(x => x.PublishedAtUtc ?? x.UpdatedAtUtc)
            .Select(x => new
            {
                x.OfferId,
                x.PublishedJson,
            })
            .ToListAsync(cancellationToken);

        Guid? selectedOfferId = null;
        var matchingOffersCount = 0;
        foreach (var definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.PublishedJson) || !ContainsMessageStartNode(definition.PublishedJson))
            {
                continue;
            }

            matchingOffersCount += 1;
            if (!selectedOfferId.HasValue)
            {
                selectedOfferId = definition.OfferId;
            }
        }

        if (matchingOffersCount > 1 && selectedOfferId.HasValue)
        {
            logger.LogInformation(
                "Project {ProjectId} has multiple message workflows. Telegram trigger will use offer {OfferId}. matches={Matches}",
                projectId,
                selectedOfferId.Value,
                matchingOffersCount);
        }

        return selectedOfferId;
    }

    private static bool ContainsMessageStartNode(string publishedJson)
    {
        try
        {
            using var document = JsonDocument.Parse(publishedJson);
            if (!TryGetPropertyIgnoreCase(document.RootElement, "nodes", out var nodes)
                || nodes.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var node in nodes.EnumerateArray())
            {
                if (!TryGetPropertyIgnoreCase(node, "type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (string.Equals(typeElement.GetString(), "MessageStart", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement source, string propertyName, out JsonElement value)
    {
        if (source.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in source.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string BuildWorkflowSourceOrderId(string chatId, long updateId)
    {
        var normalizedChatId = chatId.Trim();
        var sourceOrderId = $"tg:{normalizedChatId}:{updateId}";
        if (sourceOrderId.Length <= 160)
        {
            return sourceOrderId;
        }

        var chatTail = normalizedChatId.Length > 60
            ? normalizedChatId[^60..]
            : normalizedChatId;
        return $"tg:{chatTail}:{updateId}";
    }

    private async Task<IReadOnlyCollection<TelegramIncomingUpdate>> GetUpdatesAsync(
        TelegramProxyRuntimeConfig? proxy,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            offset = _nextOffset > 0 ? _nextOffset : (long?)null,
            timeout = Math.Clamp(_options.PollingTimeoutSeconds, 1, 50),
            limit = Math.Clamp(_options.PollingBatchSize, 1, 100),
            allowed_updates = new[] { "message" },
        };

        var proxyAttempt = await TryFetchUpdatesAsync(proxy, payload, cancellationToken);
        if (proxyAttempt.IsSuccess)
        {
            return proxyAttempt.Updates;
        }

        if (proxy is not null
            && _options.FallbackToDirectOnProxyFailure
            && IsProxyTransportFailure(proxyAttempt.Error))
        {
            var directAttempt = await TryFetchUpdatesAsync(proxy: null, payload, cancellationToken);
            if (directAttempt.IsSuccess)
            {
                return directAttempt.Updates;
            }

            throw new InvalidOperationException(
                $"telegram_updates_both_failed: proxy={proxyAttempt.Error?.Message ?? "n/a"}; direct={directAttempt.Error?.Message ?? "n/a"}");
        }

        throw new InvalidOperationException($"telegram_updates_failed: {proxyAttempt.Error?.Message ?? "unknown"}");
    }

    private async Task<UpdateFetchResult> TryFetchUpdatesAsync(
        TelegramProxyRuntimeConfig? proxy,
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            using var handler = BuildHandler(proxy);
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(_options.ApiBaseUrl),
                Timeout = TimeSpan.FromSeconds(Math.Max(10, _options.RequestTimeoutSeconds + _options.PollingTimeoutSeconds)),
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"/bot{_options.BotToken}/getUpdates")
            {
                Content = JsonContent.Create(payload),
            };

            var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return UpdateFetchResult.Failed(new InvalidOperationException($"getUpdates failed: {(int)response.StatusCode}; body={body}"));
            }

            var parsed = JsonSerializer.Deserialize<TelegramGetUpdatesResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (parsed is null || !parsed.Ok || parsed.Result is null)
            {
                return UpdateFetchResult.Success([]);
            }

            return UpdateFetchResult.Success(parsed.Result);
        }
        catch (Exception exception)
        {
            return UpdateFetchResult.Failed(exception);
        }
    }

    private static bool IsProxyTransportFailure(Exception? exception)
    {
        if (exception is null)
        {
            return false;
        }

        if (exception is TimeoutException || exception is TaskCanceledException)
        {
            return true;
        }

        if (exception is HttpRequestException)
        {
            var message = exception.Message.ToLowerInvariant();
            return message.Contains("proxy", StringComparison.Ordinal)
                   || message.Contains("connect", StringComparison.Ordinal)
                   || message.Contains("tunnel", StringComparison.Ordinal)
                   || message.Contains("502", StringComparison.Ordinal)
                   || message.Contains("timed out", StringComparison.Ordinal);
        }

        return IsProxyTransportFailure(exception.InnerException);
    }

    private static async Task<TelegramProxyRuntimeConfig?> ResolveActiveProxyAsync(
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

    private static HttpClientHandler BuildHandler(TelegramProxyRuntimeConfig? proxy)
    {
        var handler = new HttpClientHandler();
        if (proxy is null)
        {
            return handler;
        }

        var webProxy = new WebProxy(proxy.Uri);
        if (!string.IsNullOrWhiteSpace(proxy.Login))
        {
            webProxy.Credentials = new NetworkCredential(proxy.Login, proxy.Password);
        }

        handler.Proxy = webProxy;
        handler.UseProxy = true;
        return handler;
    }

    private sealed record UpdateFetchResult(bool IsSuccess, IReadOnlyCollection<TelegramIncomingUpdate> Updates, Exception? Error)
    {
        public static UpdateFetchResult Success(IReadOnlyCollection<TelegramIncomingUpdate> updates) => new(true, updates, null);

        public static UpdateFetchResult Failed(Exception error) => new(false, [], error);
    }
}

internal sealed record TelegramGetUpdatesResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("result")] TelegramIncomingUpdate[]? Result);

internal sealed record TelegramIncomingUpdate(
    [property: JsonPropertyName("update_id")] long UpdateId,
    [property: JsonPropertyName("message")] TelegramIncomingMessage? Message);

internal sealed record TelegramIncomingMessage(
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("chat")] TelegramIncomingChat Chat);

internal sealed record TelegramIncomingChat(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("title")] string? Title);
