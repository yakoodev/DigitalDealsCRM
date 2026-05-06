using DDCRM.Core.Api.AccountsManager;
using DDCRM.Core.Persistence;
using DDCRM.Core.Persistence.Entities;
using DDCRM.Shared.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DDCRM.Core.Api.Integrations;

public sealed class IntegrationWorkerRuntimeBackgroundService(
    IServiceProvider serviceProvider,
    IOptions<IntegrationWorkerRuntimeOptions> options,
    ProjectSecretCrypto crypto,
    ILogger<IntegrationWorkerRuntimeBackgroundService> logger)
    : BackgroundService
{
    private readonly IntegrationWorkerRuntimeOptions _options = options.Value;
    private readonly ProjectSecretCrypto _crypto = crypto;

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
                logger.LogError(exception, "IntegrationWorkerRuntimeBackgroundService iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var accountsManagerClient = scope.ServiceProvider.GetRequiredService<IAccountsManagerClient>();

        var now = DateTimeOffset.UtcNow;
        var items = await dbContext.IntegrationWorkerRuntimeOutbox
            .Where(x => (x.Status == "pending" || x.Status == "retry") && x.NextAttemptAtUtc <= now)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(25)
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            var normalizedOperation = item.Operation.Trim().ToLowerInvariant();
            var hasOlderPendingDuplicate = await dbContext.IntegrationWorkerRuntimeOutbox
                .AsNoTracking()
                .AnyAsync(
                    x => x.Id != item.Id
                         && x.ProjectId == item.ProjectId
                         && x.IntegrationKey == item.IntegrationKey
                         && x.RuntimeAccountId == item.RuntimeAccountId
                         && x.Operation == item.Operation
                         && (x.Status == "pending" || x.Status == "retry")
                         && (x.CreatedAtUtc < item.CreatedAtUtc
                             || (x.CreatedAtUtc == item.CreatedAtUtc && x.Id.CompareTo(item.Id) < 0)),
                    cancellationToken);
            if (hasOlderPendingDuplicate)
            {
                item.Status = "superseded";
                item.ProcessedAtUtc = DateTimeOffset.UtcNow;
                item.LastError = "Outbox duplicate superseded by older pending item.";
                continue;
            }

            var runtime = await dbContext.ProjectIntegrationWorkerRuntimes
                .SingleOrDefaultAsync(
                    x => x.ProjectId == item.ProjectId
                         && x.IntegrationKey == item.IntegrationKey
                         && x.RuntimeAccountId == item.RuntimeAccountId,
                    cancellationToken);

            if (runtime is null)
            {
                item.Status = "completed";
                item.ProcessedAtUtc = DateTimeOffset.UtcNow;
                item.LastError = null;
                continue;
            }

            try
            {
                if (!IntegrationKeys.WorkerIntegrations.Contains(item.IntegrationKey))
                {
                    throw new InvalidOperationException($"Unsupported worker integration key: {item.IntegrationKey}");
                }

                var idempotencyKey = $"worker-runtime-{item.Operation}-{item.Id:N}";
                var runtimeConfiguration = ReadRuntimeConfiguration(runtime);
                var proxyConfig = runtimeConfiguration?.ProxyConfig is null
                    ? BuildDefaultProxyConfig()
                    : ToProxyConfigDictionary(runtimeConfiguration.ProxyConfig);
                if (normalizedOperation == "provision")
                {
                    var grantIsActive = await dbContext.ProjectIntegrationGrants.AnyAsync(
                        x => x.ProjectId == item.ProjectId
                             && x.IntegrationKey == item.IntegrationKey
                             && x.Status == "active",
                        cancellationToken);
                    if (!grantIsActive)
                    {
                        item.Status = "completed";
                        item.ProcessedAtUtc = DateTimeOffset.UtcNow;
                        item.LastError = "Provision skipped: integration grant is not active.";
                        runtime.UpdatedAtUtc = DateTimeOffset.UtcNow;
                        continue;
                    }

                    try
                    {
                        await accountsManagerClient.CreateLifecycleAsync(
                            item.ProjectId,
                            runtime.RuntimeAccountId,
                            ResolveWorkerPlatform(item.IntegrationKey),
                            proxyConfig,
                            marketplaceAuth: null,
                            mailConfig: null,
                            idempotencyKey,
                            cancellationToken);
                    }
                    catch (ApiErrorException exception) when (
                        exception.StatusCode == StatusCodes.Status409Conflict
                        && IsExistingPlacementConflict(exception))
                    {
                        // Runtime account уже существует в placement — считаем provision идемпотентным и применяем update.
                        await accountsManagerClient.UpdateLifecycleAsync(
                            runtime.RuntimeAccountId,
                            proxyConfig,
                            mailConfig: null,
                            $"{idempotencyKey}:reconcile",
                            cancellationToken);
                    }

                    runtime.Status = "active";
                    runtime.LastError = null;
                    runtime.ProvisionedAtUtc = DateTimeOffset.UtcNow;
                    runtime.UpdatedAtUtc = DateTimeOffset.UtcNow;
                }
                else if (normalizedOperation == "deprovision")
                {
                    await accountsManagerClient.DeleteLifecycleAsync(
                        runtime.RuntimeAccountId,
                        idempotencyKey,
                        cancellationToken);

                    runtime.Status = "revoked";
                    runtime.LastError = null;
                    runtime.DeprovisionedAtUtc = DateTimeOffset.UtcNow;
                    runtime.UpdatedAtUtc = DateTimeOffset.UtcNow;
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported worker runtime operation: {item.Operation}");
                }

                item.Status = "completed";
                item.ProcessedAtUtc = DateTimeOffset.UtcNow;
                item.LastError = null;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Worker runtime sync failed for outbox item {OutboxId}, integration {IntegrationKey}",
                    item.Id,
                    item.IntegrationKey);

                runtime.Status = item.Operation == "deprovision"
                    ? "revoking"
                    : "pending_provision";
                runtime.LastError = exception.Message.Length > 1000
                    ? exception.Message[..1000]
                    : exception.Message;
                runtime.UpdatedAtUtc = DateTimeOffset.UtcNow;

                MarkRetry(item, exception.Message);
            }
        }

        if (items.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private string ResolveWorkerPlatform(string integrationKey)
    {
        return string.Equals(integrationKey, IntegrationKeys.SteamAccountsManager, StringComparison.OrdinalIgnoreCase)
            ? _options.SteamPlatform
            : integrationKey;
    }

    private Dictionary<string, object?> BuildDefaultProxyConfig()
    {
        return new Dictionary<string, object?>
        {
            ["host"] = _options.DefaultProxyHost,
            ["port"] = _options.DefaultProxyPort,
            ["login"] = _options.DefaultProxyLogin,
            ["password"] = _options.DefaultProxyPassword,
        };
    }

    private Dictionary<string, object?> ToProxyConfigDictionary(ProxyConfigPayload proxyConfig)
    {
        return new Dictionary<string, object?>
        {
            ["host"] = proxyConfig.Host,
            ["port"] = proxyConfig.Port,
            ["login"] = proxyConfig.Login,
            ["password"] = proxyConfig.Password,
        };
    }

    private IntegrationWorkerRuntimeConfiguration? ReadRuntimeConfiguration(ProjectIntegrationWorkerRuntimeEntity runtime)
    {
        if (string.IsNullOrWhiteSpace(runtime.ConfigurationCiphertext))
        {
            return null;
        }

        try
        {
            var json = _crypto.Decrypt(runtime.ConfigurationCiphertext);
            return JsonSerializer.Deserialize<IntegrationWorkerRuntimeConfiguration>(json, RuntimeJson.Defaults);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to read runtime configuration for project {ProjectId}, integration {IntegrationKey}.",
                runtime.ProjectId,
                runtime.IntegrationKey);
            return null;
        }
    }

    private static void MarkRetry(IntegrationWorkerRuntimeOutboxEntity item, string error)
    {
        item.AttemptCount += 1;
        item.Status = "retry";
        item.LastError = error.Length > 1000 ? error[..1000] : error;
        var delaySeconds = Math.Min(300, 5 * Math.Max(1, item.AttemptCount));
        item.NextAttemptAtUtc = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
    }

    private static bool IsExistingPlacementConflict(ApiErrorException exception)
    {
        if (exception.Details is not IReadOnlyDictionary<string, object?> details)
        {
            return false;
        }

        if (!details.TryGetValue("upstreamBody", out var upstreamBodyObj))
        {
            return false;
        }

        var upstreamBody = upstreamBodyObj as string;
        if (string.IsNullOrWhiteSpace(upstreamBody))
        {
            return false;
        }

        var decodedMessage = upstreamBody;
        try
        {
            using var json = JsonDocument.Parse(upstreamBody);
            if (json.RootElement.TryGetProperty("message", out var messageElement))
            {
                var message = messageElement.GetString();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    decodedMessage = message;
                }
            }
        }
        catch
        {
            // Ignore parse errors and use raw upstream body text.
        }

        return decodedMessage.Contains("уже существует", StringComparison.OrdinalIgnoreCase)
               || decodedMessage.Contains("already exists", StringComparison.OrdinalIgnoreCase);
    }
}
