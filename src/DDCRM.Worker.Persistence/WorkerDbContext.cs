using DDCRM.Shared.Idempotency;
using DDCRM.Worker.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DDCRM.Worker.Persistence;

public sealed class WorkerDbContext(DbContextOptions<WorkerDbContext> options)
    : DbContext(options), IIdempotencyDbContext
{
    public DbSet<WorkerListingEntity> Listings => Set<WorkerListingEntity>();

    public DbSet<WorkerOrderEntity> Orders => Set<WorkerOrderEntity>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkerListingEntity>(entity =>
        {
            entity.ToTable("worker_listings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Price).HasColumnType("numeric(18,2)");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("NOW()");
        });

        modelBuilder.Entity<WorkerOrderEntity>(entity =>
        {
            entity.ToTable("worker_orders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Total).HasColumnType("numeric(18,2)");
            entity.Property(x => x.Currency).HasMaxLength(8).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("NOW()");
        });

        modelBuilder.ConfigureIdempotencyRecord();
    }
}
