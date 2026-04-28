using Microsoft.EntityFrameworkCore;

namespace DDCRM.Shared.Idempotency;

public interface IIdempotencyDbContext
{
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
}
