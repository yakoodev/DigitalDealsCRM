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

    public DbSet<ProxyCredentialsAuditEntity> ProxyCredentialsAudits => Set<ProxyCredentialsAuditEntity>();

    public DbSet<ProjectIntegrationGrantEntity> ProjectIntegrationGrants => Set<ProjectIntegrationGrantEntity>();

    public DbSet<ProjectServiceCredentialEntity> ProjectServiceCredentials => Set<ProjectServiceCredentialEntity>();

    public DbSet<TelegramProxyProfileEntity> TelegramProxyProfiles => Set<TelegramProxyProfileEntity>();

    public DbSet<TelegramChatBindingEntity> TelegramChatBindings => Set<TelegramChatBindingEntity>();

    public DbSet<TelegramLinkCodeEntity> TelegramLinkCodes => Set<TelegramLinkCodeEntity>();

    public DbSet<NotificationOutboxEntity> NotificationOutbox => Set<NotificationOutboxEntity>();

    public DbSet<ServiceCredentialSyncOutboxEntity> ServiceCredentialSyncOutbox => Set<ServiceCredentialSyncOutboxEntity>();

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
            entity.HasMany(x => x.IntegrationGrants).WithOne(x => x.Project).HasForeignKey(x => x.ProjectId);
            entity.HasMany(x => x.ServiceCredentials).WithOne(x => x.Project).HasForeignKey(x => x.ProjectId);
            entity.HasMany(x => x.TelegramChatBindings).WithOne(x => x.Project).HasForeignKey(x => x.ProjectId);
            entity.HasMany(x => x.TelegramLinkCodes).WithOne(x => x.Project).HasForeignKey(x => x.ProjectId);
            entity.HasMany(x => x.NotificationOutboxItems).WithOne(x => x.Project).HasForeignKey(x => x.ProjectId);
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

        modelBuilder.Entity<ProxyCredentialsAuditEntity>(entity =>
        {
            entity.ToTable("proxy_credentials_audits");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Operation).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            entity.Property(x => x.RequestId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.ProjectId, x.AccountId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<ProjectIntegrationGrantEntity>(entity =>
        {
            entity.ToTable("project_integration_grants");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.IntegrationKey).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ScopesCsv).HasMaxLength(120).IsRequired();
            entity.Property(x => x.GrantedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.ProjectId, x.IntegrationKey }).IsUnique();
        });

        modelBuilder.Entity<ProjectServiceCredentialEntity>(entity =>
        {
            entity.ToTable("project_service_credentials");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.IntegrationKey).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ScopesCsv).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.SecretHashSha256).HasMaxLength(64).IsRequired();
            entity.Property(x => x.SecretMasked).HasMaxLength(12).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.ProjectId, x.IntegrationKey }).IsUnique();
        });

        modelBuilder.Entity<TelegramProxyProfileEntity>(entity =>
        {
            entity.ToTable("telegram_proxy_profiles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.ProxyType).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Host).HasMaxLength(255).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => x.Name).IsUnique();
            entity.HasIndex(x => x.IsActive).HasFilter("\"IsActive\" = TRUE");
        });

        modelBuilder.Entity<TelegramChatBindingEntity>(entity =>
        {
            entity.ToTable("telegram_chat_bindings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ChatId).HasMaxLength(120).IsRequired();
            entity.Property(x => x.BindingType).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ChatTitle).HasMaxLength(200);
            entity.Property(x => x.LinkedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.ProjectId, x.BindingType, x.ChatId }).IsUnique();
            entity.HasIndex(x => new { x.ProjectId, x.UserId, x.BindingType });
        });

        modelBuilder.Entity<TelegramLinkCodeEntity>(entity =>
        {
            entity.ToTable("telegram_link_codes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(64).IsRequired();
            entity.Property(x => x.BindingType).HasMaxLength(32).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => x.Code).IsUnique();
            entity.HasIndex(x => new { x.ProjectId, x.ExpiresAtUtc });
        });

        modelBuilder.Entity<NotificationOutboxEntity>(entity =>
        {
            entity.ToTable("notification_outbox");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Channel).HasMaxLength(32).IsRequired();
            entity.Property(x => x.EventType).HasMaxLength(120).IsRequired();
            entity.Property(x => x.DeduplicationKey).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.LastError).HasMaxLength(1000);
            entity.Property(x => x.PayloadJson).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => x.DeduplicationKey).IsUnique();
            entity.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
        });

        modelBuilder.Entity<ServiceCredentialSyncOutboxEntity>(entity =>
        {
            entity.ToTable("service_credential_sync_outbox");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.IntegrationKey).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Operation).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.LastError).HasMaxLength(1000);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
            entity.HasIndex(x => new { x.ProjectId, x.IntegrationKey, x.Operation });
        });

        modelBuilder.ConfigureIdempotencyRecord();
    }
}
