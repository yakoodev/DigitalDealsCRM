using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DDCRM.Core.Persistence;
using DDCRM.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DDCRM.Core.Api.Workflows;

public sealed class WorkflowMessagePollingBackgroundService(
    IServiceProvider serviceProvider,
    IOptions<WorkflowMessagePollingOptions> options,
    ILogger<WorkflowMessagePollingBackgroundService> logger)
    : BackgroundService
{
    private readonly WorkflowMessagePollingOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Workflow message polling disabled by configuration.");
            return;
        }

        logger.LogInformation("Workflow message polling started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Workflow message polling iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(2, _options.PollIntervalSeconds)), stoppingToken);
        }
    }

    private async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var bridge = scope.ServiceProvider.GetRequiredService<WorkflowWorkerBridgeClient>();

        var publishedDefinitions = await dbContext.WorkflowDefinitions
            .AsNoTracking()
            .Where(x => x.PublishedJson != null)
            .Select(x => new
            {
                x.ProjectId,
                x.OfferId,
                x.PublishedJson,
            })
            .ToListAsync(cancellationToken);

        var messageEnabledOffers = publishedDefinitions
            .Where(definition => !string.IsNullOrWhiteSpace(definition.PublishedJson) && ContainsMessageStartNode(definition.PublishedJson!))
            .Select(definition => new MessageEnabledOffer(definition.ProjectId, definition.OfferId))
            .Distinct()
            .ToArray();
        if (messageEnabledOffers.Length == 0)
        {
            return;
        }

        var offerIds = messageEnabledOffers
            .Select(x => x.OfferId)
            .Distinct()
            .ToArray();

        var variants = await dbContext.OfferVariants
            .AsNoTracking()
            .Where(x => x.IsActive && offerIds.Contains(x.OfferId))
            .Select(x => new
            {
                x.ProjectId,
                x.OfferId,
                x.AccountId,
                x.Platform,
                x.Priority,
            })
            .ToListAsync(cancellationToken);
        if (variants.Count == 0)
        {
            return;
        }

        var messageEnabledByProjectOffer = messageEnabledOffers
            .ToHashSet();
        var accountBindings = variants
            .Where(variant => messageEnabledByProjectOffer.Contains(new MessageEnabledOffer(variant.ProjectId, variant.OfferId)))
            .GroupBy(variant => variant.AccountId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(x => x.ProjectId)
                    .Select(projectGroup => projectGroup
                        .OrderBy(x => x.Priority)
                        .ThenBy(x => x.OfferId)
                        .ThenBy(x => x.Platform, StringComparer.OrdinalIgnoreCase)
                        .Select(x => new AccountOfferBinding(x.ProjectId, x.OfferId, x.Platform))
                        .First())
                    .Distinct()
                    .ToArray());

        var ambiguousProjectBindings = variants
            .Where(variant => messageEnabledByProjectOffer.Contains(new MessageEnabledOffer(variant.ProjectId, variant.OfferId)))
            .GroupBy(variant => new { variant.AccountId, variant.ProjectId })
            .Where(group => group.Select(x => x.OfferId).Distinct().Count() > 1)
            .Select(group => new
            {
                group.Key.AccountId,
                group.Key.ProjectId,
                OfferIds = group.Select(x => x.OfferId).Distinct().OrderBy(x => x).ToArray(),
            })
            .ToArray();
        foreach (var ambiguous in ambiguousProjectBindings)
        {
            logger.LogWarning(
                "Workflow message polling found multiple message-enabled offers for one account in one project. account={AccountId} project={ProjectId} offers={Offers}. Polling will use one deterministic offer per message.",
                ambiguous.AccountId,
                ambiguous.ProjectId,
                string.Join(",", ambiguous.OfferIds));
        }
        if (accountBindings.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var createdEvents = 0;
        var bootstrapCursorCount = 0;
        var updatedCursorCount = 0;
        var pendingProjectSourceKeys = new HashSet<string>(StringComparer.Ordinal);
        var cursorCache = new Dictionary<string, WorkflowMessageCursorEntity?>(StringComparer.Ordinal);
        foreach (var accountEntry in accountBindings)
        {
            var accountId = accountEntry.Key;
            var bindings = accountEntry.Value;

            var route = await bridge.ResolveRouteAsync(_options, accountId, cancellationToken);
            if (route is null)
            {
                continue;
            }

            var conversations = await bridge.ListConversationsAsync(
                _options,
                route,
                _options.ConversationLimit,
                _options.OnlyUnreadConversations,
                cancellationToken);
            if (conversations.Count == 0)
            {
                continue;
            }

            foreach (var conversation in conversations.Take(Math.Max(1, _options.MaxConversationsPerAccount)))
            {
                var messages = await bridge.ListMessagesAsync(
                    _options,
                    route,
                    conversation.ConversationId,
                    _options.MessagesLimit,
                    cancellationToken);
                if (messages.Count > 1)
                {
                    var distinctCreatedAt = messages
                        .Select(message => message.CreatedAtUtc)
                        .Distinct()
                        .Count();
                    if (distinctCreatedAt == 1)
                    {
                        logger.LogWarning(
                            "Workflow message polling detected unstable worker timestamps: all messages in conversation {ConversationId} share the same createdAt. account={AccountId} count={Count}",
                            conversation.ConversationId,
                            accountId,
                            messages.Count);
                    }
                }

                var candidateMessages = messages
                    .Where(IsIncomingMessage)
                    .Where(message => !string.IsNullOrWhiteSpace(message.Text))
                    .Where(message => !string.IsNullOrWhiteSpace(message.MessageId))
                    .GroupBy(message => message.MessageId.Trim(), StringComparer.Ordinal)
                    .Select(group => group.First())
                    .OrderBy(message => message.MessageId.Trim(), MessageIdComparer.Instance)
                    .ToArray();
                if (candidateMessages.Length == 0)
                {
                    continue;
                }

                var newestMessageId = candidateMessages[^1].MessageId.Trim();
                foreach (var binding in bindings)
                {
                    var cursorKey = BuildCursorKey(binding.ProjectId, accountId, conversation.ConversationId);
                    if (!cursorCache.TryGetValue(cursorKey, out var cursorEntity))
                    {
                        cursorEntity = await dbContext.WorkflowMessageCursors
                            .SingleOrDefaultAsync(
                                x => x.ProjectId == binding.ProjectId
                                     && x.AccountId == accountId
                                     && x.ConversationId == conversation.ConversationId,
                                cancellationToken);
                        cursorCache[cursorKey] = cursorEntity;
                    }

                    if (cursorEntity is null)
                    {
                        cursorEntity = new WorkflowMessageCursorEntity
                        {
                            Id = Guid.NewGuid(),
                            ProjectId = binding.ProjectId,
                            AccountId = accountId,
                            ConversationId = conversation.ConversationId,
                            LastSeenMessageId = _options.SkipFirstMessagePerConversation
                                ? newestMessageId
                                : string.Empty,
                            CreatedAtUtc = now,
                            UpdatedAtUtc = now,
                        };
                        dbContext.WorkflowMessageCursors.Add(cursorEntity);
                        cursorCache[cursorKey] = cursorEntity;

                        if (_options.SkipFirstMessagePerConversation)
                        {
                            bootstrapCursorCount += 1;
                            continue;
                        }
                    }

                    var unseenMessages = candidateMessages
                        .Where(message => CompareMessageIds(message.MessageId, cursorEntity.LastSeenMessageId) > 0)
                        .ToArray();
                    if (unseenMessages.Length == 0)
                    {
                        continue;
                    }

                    var selectedMessages = unseenMessages
                        .Take(Math.Max(1, _options.MaxMessagesPerConversationPerPoll))
                        .ToArray();
                    var highestSeenId = cursorEntity.LastSeenMessageId;

                    foreach (var message in selectedMessages)
                    {
                        var normalizedMessageId = message.MessageId.Trim();
                        var sourceOrderId = BuildSourceOrderId(accountId, conversation.ConversationId, normalizedMessageId);
                        var projectSourceKey = BuildProjectSourceKey(binding.ProjectId, sourceOrderId);
                        if (!pendingProjectSourceKeys.Add(projectSourceKey))
                        {
                            if (CompareMessageIds(normalizedMessageId, highestSeenId) > 0)
                            {
                                highestSeenId = normalizedMessageId;
                            }

                            continue;
                        }

                        var alreadyExists = await dbContext.WorkflowTriggerEvents
                            .AsNoTracking()
                            .AnyAsync(
                                x => x.ProjectId == binding.ProjectId
                                     && x.SourceOrderId == sourceOrderId,
                                cancellationToken);
                        if (!alreadyExists)
                        {
                            var triggerEvent = new WorkflowTriggerEventEntity
                            {
                                Id = Guid.NewGuid(),
                                ProjectId = binding.ProjectId,
                                OfferId = binding.OfferId,
                                Source = "message-worker-polling",
                                SourceOrderId = sourceOrderId,
                                BuyerId = conversation.PeerId,
                                PayloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
                                {
                                    ["platform"] = binding.Platform,
                                    ["messageText"] = message.Text,
                                    ["accountId"] = accountId.ToString(),
                                    ["conversationId"] = conversation.ConversationId,
                                    ["messageId"] = normalizedMessageId,
                                    ["peerId"] = conversation.PeerId,
                                    ["peerName"] = conversation.PeerName,
                                    ["messageCreatedAt"] = message.CreatedAtUtc?.ToString("O"),
                                }),
                                Status = "accepted",
                                CreatedAtUtc = now,
                            };

                            dbContext.WorkflowTriggerEvents.Add(triggerEvent);
                            dbContext.WorkflowOutbox.Add(new WorkflowOutboxEntity
                            {
                                Id = Guid.NewGuid(),
                                TriggerEventId = triggerEvent.Id,
                                ProjectId = binding.ProjectId,
                                OfferId = binding.OfferId,
                                Status = "pending",
                                AttemptCount = 0,
                                NextAttemptAtUtc = now,
                                CreatedAtUtc = now,
                            });

                            createdEvents += 1;
                        }

                        if (CompareMessageIds(normalizedMessageId, highestSeenId) > 0)
                        {
                            highestSeenId = normalizedMessageId;
                        }
                    }

                    if (CompareMessageIds(highestSeenId, cursorEntity.LastSeenMessageId) > 0)
                    {
                        cursorEntity.LastSeenMessageId = highestSeenId;
                        cursorEntity.UpdatedAtUtc = now;
                        updatedCursorCount += 1;
                    }
                }
            }
        }

        if (createdEvents > 0 || bootstrapCursorCount > 0 || updatedCursorCount > 0)
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                if (createdEvents > 0)
                {
                    logger.LogInformation("Workflow message polling enqueued {Count} trigger events.", createdEvents);
                }

                if (bootstrapCursorCount > 0)
                {
                    logger.LogInformation(
                        "Workflow message polling initialized {Count} conversation cursors in bootstrap mode.",
                        bootstrapCursorCount);
                }

                if (updatedCursorCount > 0)
                {
                    logger.LogDebug(
                        "Workflow message polling advanced {Count} conversation cursors.",
                        updatedCursorCount);
                }
            }
            catch (DbUpdateException exception) when (IsDuplicateMessageTriggerViolation(exception))
            {
                logger.LogInformation(
                    exception,
                    "Workflow message polling skipped duplicate trigger events in concurrent processing.");
            }
        }
    }

    private static bool ContainsMessageStartNode(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!TryGetPropertyIgnoreCase(document.RootElement, "nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var node in nodes.EnumerateArray())
            {
                if (!TryGetPropertyIgnoreCase(node, "type", out var typeProperty) || typeProperty.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (string.Equals(typeProperty.GetString(), WorkflowNodeTypes.MessageStart, StringComparison.Ordinal))
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

    private static bool IsIncomingMessage(WorkflowConversationMessage message)
    {
        var direction = message.Direction.Trim().ToLowerInvariant();
        return direction is "in" or "incoming" or "inbound" or "buyer" or "customer";
    }

    private static string BuildSourceOrderId(Guid accountId, string conversationId, string messageId)
    {
        var raw = $"worker:{accountId:N}:{conversationId.Trim()}:{messageId.Trim()}";
        if (raw.Length <= 160)
        {
            return raw;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        return $"worker:{accountId:N}:{hash[..80]}";
    }

    private static string BuildProjectSourceKey(Guid projectId, string sourceOrderId)
    {
        return $"{projectId:N}:{sourceOrderId}";
    }

    private static string BuildCursorKey(Guid projectId, Guid accountId, string conversationId)
    {
        return $"{projectId:N}:{accountId:N}:{conversationId.Trim()}";
    }

    private static int CompareMessageIds(string? leftRaw, string? rightRaw)
    {
        var left = leftRaw?.Trim() ?? string.Empty;
        var right = rightRaw?.Trim() ?? string.Empty;

        if (ulong.TryParse(left, out var leftNumber) && ulong.TryParse(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        return string.CompareOrdinal(left, right);
    }

    private static bool IsDuplicateMessageTriggerViolation(DbUpdateException exception)
    {
        if (exception.InnerException is not PostgresException postgresException)
        {
            return false;
        }

        if (!string.Equals(postgresException.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal))
        {
            return false;
        }

        return postgresException.ConstraintName?.Contains(
            "IX_workflow_trigger_events_ProjectId_SourceOrderId",
            StringComparison.OrdinalIgnoreCase) == true;
    }

    private readonly record struct MessageEnabledOffer(Guid ProjectId, Guid OfferId);

    private readonly record struct AccountOfferBinding(Guid ProjectId, Guid OfferId, string Platform);

    private sealed class MessageIdComparer : IComparer<string>
    {
        public static MessageIdComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            return CompareMessageIds(x, y);
        }
    }
}
