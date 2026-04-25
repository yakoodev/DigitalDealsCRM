using DDCRM.AccountsManager.Persistence.Entities;
using DDCRM.Shared.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace DDCRM.AccountsManager.Persistence;

public sealed class AccountsManagerDbContext(DbContextOptions<AccountsManagerDbContext> options)
    : DbContext(options), IIdempotencyDbContext
{
    public DbSet<WorkerPlacementEntity> WorkerPlacements => Set<WorkerPlacementEntity>();

    public DbSet<WorkerServerEntity> WorkerServers => Set<WorkerServerEntity>();

    public DbSet<LifecycleAuditEntity> LifecycleAudits => Set<LifecycleAuditEntity>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkerPlacementEntity>(entity =>
        {
            entity.ToTable("worker_placements");
            entity.HasKey(x => x.AccountId);
            entity.Property(x => x.Platform).HasMaxLength(80).IsRequired();
            entity.Property(x => x.WorkerId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ServerId).HasMaxLength(120).IsRequired();
            entity.Property(x => x.PodId).HasMaxLength(120);
            entity.Property(x => x.LifecycleStatus).HasMaxLength(32).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => x.ProjectId);
        });

        modelBuilder.Entity<WorkerServerEntity>(entity =>
        {
            entity.ToTable("worker_servers");
            entity.HasKey(x => x.ServerId);
            entity.Property(x => x.ServerId).HasMaxLength(120).IsRequired();
            entity.Property(x => x.BaseUrlTemplate).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Health).HasMaxLength(32).IsRequired();
            entity.Property(x => x.MetadataJson).HasMaxLength(4000);
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.Status, x.Health });
        });

        modelBuilder.Entity<LifecycleAuditEntity>(entity =>
        {
            entity.ToTable("lifecycle_audits");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Operation).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Actor).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(400).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.AccountId, x.CreatedAtUtc });
        });

        modelBuilder.ConfigureIdempotencyRecord();
    }
}
