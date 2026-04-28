using System.Text.Json;
using DDCRM.Core.Persistence;
using DDCRM.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DDCRM.Core.Api.Integrations;

public sealed class NotificationOutboxBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<NotificationOutboxBackgroundService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "NotificationOutboxBackgroundService iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<ProjectSecretCrypto>();
        var sender = scope.ServiceProvider.GetRequiredService<TelegramNotificationSender>();

        var now = DateTimeOffset.UtcNow;
        var items = await dbContext.NotificationOutbox
            .Where(x => x.Channel == IntegrationKeys.Telegram && (x.Status == "pending" || x.Status == "retry") && x.NextAttemptAtUtc <= now)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(25)
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            try
            {
                var chatIds = await ResolveChatTargetsAsync(dbContext, item.ProjectId, cancellationToken);
                if (chatIds.Count == 0)
                {
                    item.Status = "completed";
                    item.ProcessedAtUtc = DateTimeOffset.UtcNow;
                    item.LastError = null;
                    continue;
                }

                var message = ResolveMessage(item.PayloadJson, item.EventType);
                var proxy = await ResolveActiveProxyAsync(dbContext, crypto, cancellationToken);
                foreach (var chatId in chatIds)
                {
                    await sender.SendMessageAsync(chatId, message, proxy, cancellationToken);
                }

                item.Status = "completed";
                item.ProcessedAtUtc = DateTimeOffset.UtcNow;
                item.LastError = null;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Telegram outbox delivery failed for item {OutboxId}", item.Id);
                MarkRetry(item, exception.Message);
            }
        }

        if (items.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<IReadOnlyCollection<string>> ResolveChatTargetsAsync(
        CoreDbContext dbContext,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var memberUserIds = await dbContext.ProjectMembers
            .Where(x => x.ProjectId == projectId)
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken);

        var groupChats = await dbContext.TelegramChatBindings
            .Where(x => x.ProjectId == projectId && x.BindingType == "group")
            .Select(x => x.ChatId)
            .ToListAsync(cancellationToken);

        var userChats = await dbContext.TelegramChatBindings
            .Where(x => x.ProjectId == projectId && x.BindingType == "user" && x.UserId.HasValue && memberUserIds.Contains(x.UserId.Value))
            .Select(x => x.ChatId)
            .ToListAsync(cancellationToken);

        return groupChats
            .Concat(userChats)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<TelegramProxyRuntimeConfig?> ResolveActiveProxyAsync(
        CoreDbContext dbContext,
        ProjectSecretCrypto crypto,
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
        var login = string.IsNullOrWhiteSpace(proxy.LoginCiphertext) ? null : crypto.Decrypt(proxy.LoginCiphertext);
        var password = string.IsNullOrWhiteSpace(proxy.PasswordCiphertext) ? null : crypto.Decrypt(proxy.PasswordCiphertext);
        return new TelegramProxyRuntimeConfig(uri, login, password);
    }

    private static string ResolveMessage(string payloadJson, string eventType)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.TryGetProperty("message", out var messageElement)
                && messageElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(messageElement.GetString()))
            {
                return messageElement.GetString()!.Trim();
            }
        }
        catch
        {
            // ignore malformed payload and fallback to event type.
        }

        return $"[DDCRM] Критичное событие: {eventType}";
    }

    private static void MarkRetry(NotificationOutboxEntity item, string error)
    {
        item.AttemptCount += 1;
        item.Status = "retry";
        item.LastError = error.Length > 1000 ? error[..1000] : error;
        var delaySeconds = Math.Min(300, 5 * Math.Max(1, item.AttemptCount));
        item.NextAttemptAtUtc = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
    }
}
