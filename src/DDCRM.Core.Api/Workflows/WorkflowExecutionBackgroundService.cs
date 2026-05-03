using System.Text.Json;
using DDCRM.Core.Persistence;
using DDCRM.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DDCRM.Core.Api.Workflows;

public sealed class WorkflowExecutionBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<WorkflowExecutionBackgroundService> logger)
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
                logger.LogError(exception, "WorkflowExecutionBackgroundService iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var executionEngine = scope.ServiceProvider.GetRequiredService<WorkflowExecutionEngine>();

        var now = DateTimeOffset.UtcNow;
        var items = await dbContext.WorkflowOutbox
            .Where(x => (x.Status == "pending" || x.Status == "retry") && x.NextAttemptAtUtc <= now)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(25)
            .ToListAsync(cancellationToken);

        foreach (var outboxItem in items)
        {
            var triggerEvent = await dbContext.WorkflowTriggerEvents
                .SingleOrDefaultAsync(x => x.Id == outboxItem.TriggerEventId, cancellationToken);
            if (triggerEvent is null)
            {
                outboxItem.Status = "completed";
                outboxItem.ProcessedAtUtc = DateTimeOffset.UtcNow;
                outboxItem.LastError = "Trigger event отсутствует.";
                continue;
            }

            var definition = await dbContext.WorkflowDefinitions
                .SingleOrDefaultAsync(
                    x => x.ProjectId == triggerEvent.ProjectId
                         && x.OfferId == triggerEvent.OfferId,
                    cancellationToken);
            if (definition is null || string.IsNullOrWhiteSpace(definition.PublishedJson))
            {
                outboxItem.Status = "failed";
                outboxItem.ProcessedAtUtc = DateTimeOffset.UtcNow;
                outboxItem.LastError = "Published workflow не найден.";
                triggerEvent.Status = "failed";
                triggerEvent.ProcessedAtUtc = DateTimeOffset.UtcNow;
                continue;
            }

            var alreadyCompleted = await dbContext.WorkflowExecutions.AnyAsync(
                x => x.TriggerEventId == triggerEvent.Id && x.Status == "completed",
                cancellationToken);
            if (alreadyCompleted)
            {
                outboxItem.Status = "completed";
                outboxItem.ProcessedAtUtc = DateTimeOffset.UtcNow;
                outboxItem.LastError = null;
                triggerEvent.Status = "processed";
                triggerEvent.ProcessedAtUtc = DateTimeOffset.UtcNow;
                continue;
            }

            var execution = new WorkflowExecutionEntity
            {
                Id = Guid.NewGuid(),
                ProjectId = triggerEvent.ProjectId,
                OfferId = triggerEvent.OfferId,
                WorkflowDefinitionId = definition.Id,
                TriggerEventId = triggerEvent.Id,
                SourceOrderId = triggerEvent.SourceOrderId,
                WorkflowVersion = Math.Max(1, definition.PublishedVersion),
                Status = "running",
                StartedAtUtc = DateTimeOffset.UtcNow,
            };
            dbContext.WorkflowExecutions.Add(execution);
            triggerEvent.Status = "processing";
            await dbContext.SaveChangesAsync(cancellationToken);

            try
            {
                var output = await executionEngine.ExecuteAsync(
                    dbContext,
                    execution,
                    definition,
                    triggerEvent,
                    cancellationToken);

                execution.Status = "completed";
                execution.FinishedAtUtc = DateTimeOffset.UtcNow;
                execution.LastError = null;
                execution.OutputJson = JsonSerializer.Serialize(output);

                triggerEvent.Status = "processed";
                triggerEvent.ProcessedAtUtc = DateTimeOffset.UtcNow;

                outboxItem.Status = "completed";
                outboxItem.ProcessedAtUtc = DateTimeOffset.UtcNow;
                outboxItem.LastError = null;
            }
            catch (Exception exception)
            {
                execution.Status = "failed";
                execution.FinishedAtUtc = DateTimeOffset.UtcNow;
                execution.LastError = exception.Message.Length > 1000
                    ? exception.Message[..1000]
                    : exception.Message;

                triggerEvent.Status = "failed";
                triggerEvent.ProcessedAtUtc = DateTimeOffset.UtcNow;

                var maxRetries = Math.Clamp(definition.MaxRetries, 0, 20);
                outboxItem.AttemptCount += 1;
                outboxItem.LastError = exception.Message.Length > 1000
                    ? exception.Message[..1000]
                    : exception.Message;

                if (outboxItem.AttemptCount > maxRetries)
                {
                    outboxItem.Status = "failed";
                    outboxItem.ProcessedAtUtc = DateTimeOffset.UtcNow;
                }
                else
                {
                    outboxItem.Status = "retry";
                    outboxItem.NextAttemptAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, 5 * outboxItem.AttemptCount));
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
