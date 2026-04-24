using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace DDCRM.Shared.Idempotency;

public sealed record IdempotentExecutionResult(int StatusCode, object Payload);

public sealed class IdempotencyExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IResult> ExecuteAsync<TContext>(
        TContext dbContext,
        string scope,
        string idempotencyKey,
        Func<CancellationToken, Task<IdempotentExecutionResult>> action,
        CancellationToken cancellationToken)
        where TContext : DbContext, IIdempotencyDbContext
    {
        var existing = await dbContext.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Scope == scope && x.Key == idempotencyKey,
                cancellationToken);

        if (existing is not null)
        {
            return CreateStoredResult(existing);
        }

        var result = await action(cancellationToken);
        var payloadJson = JsonSerializer.Serialize(result.Payload, JsonOptions);

        dbContext.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            Scope = scope,
            Key = idempotencyKey,
            StatusCode = result.StatusCode,
            PayloadJson = payloadJson,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            existing = await dbContext.IdempotencyRecords
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.Scope == scope && x.Key == idempotencyKey,
                    cancellationToken);

            if (existing is not null)
            {
                return CreateStoredResult(existing);
            }

            throw;
        }

        return Results.Json(result.Payload, statusCode: result.StatusCode);
    }

    private static IResult CreateStoredResult(IdempotencyRecord record)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(record.PayloadJson, JsonOptions);
        return Results.Json(payload, statusCode: record.StatusCode);
    }
}
