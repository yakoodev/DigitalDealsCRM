using DDCRM.Core.Persistence;
using DDCRM.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DDCRM.Core.Api.Integrations;

public sealed class ServiceCredentialSyncBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<ServiceCredentialSyncBackgroundService> logger)
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
                logger.LogError(exception, "ServiceCredentialSyncBackgroundService iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var registry = scope.ServiceProvider.GetRequiredService<ProjectServiceIntegrationRegistry>();
        var crypto = scope.ServiceProvider.GetRequiredService<ProjectSecretCrypto>();

        var now = DateTimeOffset.UtcNow;
        var items = await dbContext.ServiceCredentialSyncOutbox
            .Where(x => (x.Status == "pending" || x.Status == "retry") && x.NextAttemptAtUtc <= now)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(25)
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            if (!registry.TryGet(item.IntegrationKey, out var client))
            {
                MarkRetry(item, "integration client not registered");
                continue;
            }

            try
            {
                var credential = await dbContext.ProjectServiceCredentials
                    .SingleOrDefaultAsync(x => x.Id == item.CredentialId, cancellationToken);

                if (credential is null)
                {
                    item.Status = "completed";
                    item.ProcessedAtUtc = DateTimeOffset.UtcNow;
                    continue;
                }

                var idempotencyKey = $"sync-{item.Id:N}";

                if (string.Equals(item.Operation, "grant", StringComparison.Ordinal))
                {
                    var plainToken = crypto.Decrypt(credential.SecretCiphertext);
                    var scopes = SplitScopes(credential.ScopesCsv);
                    await client.UpsertProjectTokenAsync(item.ProjectId, plainToken, scopes, idempotencyKey, cancellationToken);
                    credential.Status = "active";
                    credential.UpdatedAtUtc = DateTimeOffset.UtcNow;
                }
                else
                {
                    await client.RevokeProjectTokenAsync(item.ProjectId, idempotencyKey, cancellationToken);
                    credential.Status = "revoked";
                    credential.RevokedAtUtc = DateTimeOffset.UtcNow;
                    credential.UpdatedAtUtc = DateTimeOffset.UtcNow;
                }

                item.Status = "completed";
                item.ProcessedAtUtc = DateTimeOffset.UtcNow;
                item.LastError = null;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Integration sync failed for item {OutboxId}", item.Id);
                MarkRetry(item, exception.Message);
            }
        }

        if (items.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static IReadOnlyCollection<string> SplitScopes(string scopesCsv)
    {
        return scopesCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void MarkRetry(ServiceCredentialSyncOutboxEntity item, string error)
    {
        item.AttemptCount += 1;
        item.Status = "retry";
        item.LastError = error.Length > 1000 ? error[..1000] : error;
        var delaySeconds = Math.Min(300, 5 * Math.Max(1, item.AttemptCount));
        item.NextAttemptAtUtc = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
    }
}
