using DDCRM.Core.Persistence.Entities;
using DDCRM.Shared.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace DDCRM.Core.Persistence;

public sealed class CoreDbContext(DbContextOptions<CoreDbContext> options)
    : DbContext(options), IIdempotencyDbContext
{
    public DbSet<ProjectEntity> Projects => Set<ProjectEntity>();

    public DbSet<ProjectMemberEntity> ProjectMembers => Set<ProjectMemberEntity>();

    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();

    public DbSet<MembershipCacheInvalidationAuditEntity> MembershipCacheInvalidationAudits => Set<MembershipCacheInvalidationAuditEntity>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectEntity>(entity =>
        {
            entity.ToTable("projects");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasMany(x => x.Members).WithOne(x => x.Project).HasForeignKey(x => x.ProjectId);
            entity.HasMany(x => x.Accounts).WithOne(x => x.Project).HasForeignKey(x => x.ProjectId);
        });

        modelBuilder.Entity<ProjectMemberEntity>(entity =>
        {
            entity.ToTable("project_members");
            entity.HasKey(x => new { x.ProjectId, x.UserId });
            entity.Property(x => x.Role).HasMaxLength(32).IsRequired();
            entity.Property(x => x.JoinedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.ProjectId, x.Role }).HasFilter("\"Role\" = 'owner'").IsUnique();
        });

        modelBuilder.Entity<AccountEntity>(entity =>
        {
            entity.ToTable("accounts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Platform).HasMaxLength(80).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.BusinessStatus).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ProxyHostMasked).HasMaxLength(255);
            entity.Property(x => x.ProxyLoginMasked).HasMaxLength(255);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.ProjectId, x.Platform, x.DisplayName });
        });

        modelBuilder.Entity<MembershipCacheInvalidationAuditEntity>(entity =>
        {
            entity.ToTable("membership_cache_invalidation_audits");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Actor).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => x.CreatedAtUtc);
        });

        modelBuilder.ConfigureIdempotencyRecord();
    }
}
