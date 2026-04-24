using DDCRM.RouteRegistry.Persistence.Entities;
using DDCRM.Shared.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace DDCRM.RouteRegistry.Persistence;

public sealed class RouteRegistryDbContext(DbContextOptions<RouteRegistryDbContext> options)
    : DbContext(options), IIdempotencyDbContext
{
    public DbSet<RouteRecordEntity> RouteRecords => Set<RouteRecordEntity>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RouteRecordEntity>(entity =>
        {
            entity.ToTable("route_registry_records");
            entity.HasKey(x => x.AccountId);
            entity.Property(x => x.RouteKey).HasMaxLength(150).IsRequired();
            entity.Property(x => x.ServerId).HasMaxLength(120).IsRequired();
            entity.Property(x => x.WorkerId).HasMaxLength(120).IsRequired();
            entity.Property(x => x.PodId).HasMaxLength(120);
            entity.Property(x => x.RouteVersion).IsRequired();
            entity.Property(x => x.IsActive).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => x.RouteKey).IsUnique();
        });

        modelBuilder.ConfigureIdempotencyRecord();
    }
}
