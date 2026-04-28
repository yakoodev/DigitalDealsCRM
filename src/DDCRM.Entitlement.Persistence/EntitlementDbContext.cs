using DDCRM.Entitlement.Persistence.Entities;
using DDCRM.Shared.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace DDCRM.Entitlement.Persistence;

public sealed class EntitlementDbContext(DbContextOptions<EntitlementDbContext> options)
    : DbContext(options), IIdempotencyDbContext
{
    public DbSet<EntitlementSnapshotEntity> Snapshots => Set<EntitlementSnapshotEntity>();

    public DbSet<EntitlementOverrideEntity> Overrides => Set<EntitlementOverrideEntity>();

    public DbSet<EntitlementAuditEntity> Audits => Set<EntitlementAuditEntity>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EntitlementSnapshotEntity>(entity =>
        {
            entity.ToTable("entitlement_snapshots");
            entity.HasKey(x => x.ProjectId);
            entity.Property(x => x.State).HasMaxLength(32).IsRequired();
            entity.Property(x => x.DataJson).IsRequired();
            entity.Property(x => x.CalculatedAtUtc).HasDefaultValueSql("NOW()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("NOW()");
        });

        modelBuilder.Entity<EntitlementOverrideEntity>(entity =>
        {
            entity.ToTable("entitlement_overrides");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Actor).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.DataJson).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.ProjectId, x.Status, x.ExpiresAtUtc });
        });

        modelBuilder.Entity<EntitlementAuditEntity>(entity =>
        {
            entity.ToTable("entitlement_audits");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Operation).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Actor).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Details).HasMaxLength(400).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.ProjectId, x.CreatedAtUtc });
        });

        modelBuilder.ConfigureIdempotencyRecord();
    }
}
