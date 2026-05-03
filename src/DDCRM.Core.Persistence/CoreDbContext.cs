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

    public DbSet<ProjectIntegrationWorkerRuntimeEntity> ProjectIntegrationWorkerRuntimes => Set<ProjectIntegrationWorkerRuntimeEntity>();

    public DbSet<ProjectCustomHttpIntegrationEntity> ProjectCustomHttpIntegrations => Set<ProjectCustomHttpIntegrationEntity>();

    public DbSet<OfferEntity> Offers => Set<OfferEntity>();

    public DbSet<OfferVariantEntity> OfferVariants => Set<OfferVariantEntity>();

    public DbSet<WorkflowDefinitionEntity> WorkflowDefinitions => Set<WorkflowDefinitionEntity>();

    public DbSet<WorkflowTriggerEventEntity> WorkflowTriggerEvents => Set<WorkflowTriggerEventEntity>();

    public DbSet<WorkflowOutboxEntity> WorkflowOutbox => Set<WorkflowOutboxEntity>();

    public DbSet<WorkflowExecutionEntity> WorkflowExecutions => Set<WorkflowExecutionEntity>();

    public DbSet<WorkflowExecutionStepEntity> WorkflowExecutionSteps => Set<WorkflowExecutionStepEntity>();

    public DbSet<WorkflowMessageCursorEntity> WorkflowMessageCursors => Set<WorkflowMessageCursorEntity>();

    public DbSet<AdminCustomHttpAllowlistEntity> AdminCustomHttpAllowlist => Set<AdminCustomHttpAllowlistEntity>();

    public DbSet<TelegramProxyProfileEntity> TelegramProxyProfiles => Set<TelegramProxyProfileEntity>();

    public DbSet<TelegramChatBindingEntity> TelegramChatBindings => Set<TelegramChatBindingEntity>();

    public DbSet<TelegramLinkCodeEntity> TelegramLinkCodes => Set<TelegramLinkCodeEntity>();

    public DbSet<NotificationOutboxEntity> NotificationOutbox => Set<NotificationOutboxEntity>();

    public DbSet<ServiceCredentialSyncOutboxEntity> ServiceCredentialSyncOutbox => Set<ServiceCredentialSyncOutboxEntity>();

    public DbSet<IntegrationWorkerRuntimeOutboxEntity> IntegrationWorkerRuntimeOutbox => Set<IntegrationWorkerRuntimeOutboxEntity>();

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
            entity.HasMany(x => x.IntegrationWorkerRuntimes).WithOne(x => x.Project).HasForeignKey(x => x.ProjectId);
            entity.HasMany(x => x.CustomHttpIntegrations).WithOne(x => x.Project).HasForeignKey(x => x.ProjectId);
            entity.HasMany(x => x.Offers).WithOne(x => x.Project).HasForeignKey(x => x.ProjectId);
            entity.HasMany(x => x.OfferVariants).WithOne().HasForeignKey(x => x.ProjectId);
            entity.HasMany(x => x.WorkflowDefinitions).WithOne(x => x.Project).HasForeignKey(x => x.ProjectId);
            entity.HasMany(x => x.WorkflowTriggerEvents).WithOne(x => x.Project).HasForeignKey(x => x.ProjectId);
            entity.HasMany(x => x.WorkflowExecutions).WithOne(x => x.Project).HasForeignKey(x => x.ProjectId);
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

        modelBuilder.Entity<ProjectIntegrationWorkerRuntimeEntity>(entity =>
        {
            entity.ToTable("project_integration_worker_runtimes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.IntegrationKey).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.LastError).HasMaxLength(1000);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.ProjectId, x.IntegrationKey }).IsUnique();
            entity.HasIndex(x => x.RuntimeAccountId).IsUnique();
        });

        modelBuilder.Entity<ProjectCustomHttpIntegrationEntity>(entity =>
        {
            entity.ToTable("project_custom_http_integrations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.BaseUrl).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.BearerTokenMasked).HasMaxLength(24).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.ProjectId, x.Name }).IsUnique();
            entity.HasIndex(x => new { x.ProjectId, x.Status });
        });

        modelBuilder.Entity<OfferEntity>(entity =>
        {
            entity.ToTable("offers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.ProjectId, x.Name });
        });

        modelBuilder.Entity<OfferVariantEntity>(entity =>
        {
            entity.ToTable("offer_variants");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.WorkerProductId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Platform).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ObservedTitle).HasMaxLength(400).IsRequired();
            entity.Property(x => x.ObservedDescription).HasMaxLength(4000);
            entity.Property(x => x.ObservedCurrency).HasMaxLength(8).IsRequired();
            entity.Property(x => x.ObservedPrice).HasPrecision(18, 2);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.OfferId, x.AccountId, x.WorkerProductId }).IsUnique();
            entity.HasIndex(x => new { x.ProjectId, x.AccountId });
            entity.HasOne(x => x.Offer).WithMany(x => x.Variants).HasForeignKey(x => x.OfferId);
        });

        modelBuilder.Entity<WorkflowDefinitionEntity>(entity =>
        {
            entity.ToTable("workflow_definitions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DraftJson).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => x.OfferId).IsUnique();
            entity.HasOne(x => x.Offer).WithMany(x => x.WorkflowDefinitions).HasForeignKey(x => x.OfferId);
        });

        modelBuilder.Entity<WorkflowTriggerEventEntity>(entity =>
        {
            entity.ToTable("workflow_trigger_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Source).HasMaxLength(80).IsRequired();
            entity.Property(x => x.SourceOrderId).HasMaxLength(160).IsRequired();
            entity.Property(x => x.BuyerId).HasMaxLength(160);
            entity.Property(x => x.PayloadJson).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.ProjectId, x.SourceOrderId }).IsUnique();
            entity.HasIndex(x => new { x.ProjectId, x.OfferId, x.CreatedAtUtc });
            entity.HasOne(x => x.Offer).WithMany(x => x.WorkflowTriggerEvents).HasForeignKey(x => x.OfferId);
        });

        modelBuilder.Entity<WorkflowOutboxEntity>(entity =>
        {
            entity.ToTable("workflow_outbox");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.LastError).HasMaxLength(1000);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
            entity.HasIndex(x => x.TriggerEventId);
            entity.HasOne(x => x.TriggerEvent).WithMany(x => x.OutboxItems).HasForeignKey(x => x.TriggerEventId);
        });

        modelBuilder.Entity<WorkflowExecutionEntity>(entity =>
        {
            entity.ToTable("workflow_executions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceOrderId).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.LastError).HasMaxLength(1000);
            entity.Property(x => x.StartedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.ProjectId, x.OfferId, x.StartedAtUtc });
            entity.HasIndex(x => x.TriggerEventId);
            entity.HasOne(x => x.Offer).WithMany(x => x.WorkflowExecutions).HasForeignKey(x => x.OfferId);
            entity.HasOne(x => x.WorkflowDefinition).WithMany(x => x.Executions).HasForeignKey(x => x.WorkflowDefinitionId);
            entity.HasOne(x => x.TriggerEvent).WithMany(x => x.Executions).HasForeignKey(x => x.TriggerEventId);
        });

        modelBuilder.Entity<WorkflowExecutionStepEntity>(entity =>
        {
            entity.ToTable("workflow_execution_steps");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.NodeId).HasMaxLength(120).IsRequired();
            entity.Property(x => x.NodeType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Error).HasMaxLength(1000);
            entity.Property(x => x.StartedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.ExecutionId, x.StepIndex });
            entity.HasOne(x => x.Execution).WithMany(x => x.Steps).HasForeignKey(x => x.ExecutionId);
        });

        modelBuilder.Entity<WorkflowMessageCursorEntity>(entity =>
        {
            entity.ToTable("workflow_message_cursors");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ConversationId).HasMaxLength(160).IsRequired();
            entity.Property(x => x.LastSeenMessageId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.ProjectId, x.AccountId, x.ConversationId }).IsUnique();
            entity.HasIndex(x => new { x.ProjectId, x.UpdatedAtUtc });
            entity.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId);
        });

        modelBuilder.Entity<AdminCustomHttpAllowlistEntity>(entity =>
        {
            entity.ToTable("admin_custom_http_allowlist");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.HostPattern).HasMaxLength(255).IsRequired();
            entity.Property(x => x.Note).HasMaxLength(500);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => x.HostPattern).IsUnique();
            entity.HasIndex(x => x.IsActive);
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

        modelBuilder.Entity<IntegrationWorkerRuntimeOutboxEntity>(entity =>
        {
            entity.ToTable("integration_worker_runtime_outbox");
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
