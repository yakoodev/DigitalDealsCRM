using DDCRM.Billing.Persistence.Entities;
using DDCRM.Shared.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace DDCRM.Billing.Persistence;

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options)
    : DbContext(options), IIdempotencyDbContext
{
    public DbSet<BillingPaymentEntity> Payments => Set<BillingPaymentEntity>();

    public DbSet<BillingRefundEntity> Refunds => Set<BillingRefundEntity>();

    public DbSet<BillingSubscriptionEntity> Subscriptions => Set<BillingSubscriptionEntity>();

    public DbSet<BillingWebhookEventEntity> WebhookEvents => Set<BillingWebhookEventEntity>();

    public DbSet<BillingAuditEntity> Audits => Set<BillingAuditEntity>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BillingPaymentEntity>(entity =>
        {
            entity.ToTable("billing_payments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Amount).HasColumnType("numeric(18,2)");
            entity.Property(x => x.Currency).HasMaxLength(8).IsRequired();
            entity.Property(x => x.ProviderPaymentId).HasMaxLength(120).IsRequired();
            entity.Property(x => x.PlanKey).HasMaxLength(120);
            entity.Property(x => x.SourceOperation).HasMaxLength(64).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => x.ProjectId);
            entity.HasIndex(x => x.ProviderPaymentId).IsUnique();
        });

        modelBuilder.Entity<BillingRefundEntity>(entity =>
        {
            entity.ToTable("billing_refunds");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasColumnType("numeric(18,2)");
            entity.Property(x => x.Status).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(200);
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => x.ProjectId);
            entity.HasIndex(x => x.PaymentId);
        });

        modelBuilder.Entity<BillingSubscriptionEntity>(entity =>
        {
            entity.ToTable("billing_subscriptions");
            entity.HasKey(x => x.ProjectId);
            entity.Property(x => x.Status).HasMaxLength(80).IsRequired();
            entity.Property(x => x.PlanKey).HasMaxLength(120).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("NOW()");
        });

        modelBuilder.Entity<BillingWebhookEventEntity>(entity =>
        {
            entity.ToTable("billing_webhook_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventId).HasMaxLength(160).IsRequired();
            entity.Property(x => x.EventType).HasMaxLength(120).IsRequired();
            entity.Property(x => x.PayloadJson).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.Property(x => x.ProcessedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => x.EventId).IsUnique();
            entity.HasIndex(x => x.ProjectId);
        });

        modelBuilder.Entity<BillingAuditEntity>(entity =>
        {
            entity.ToTable("billing_audits");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Operation).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Actor).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(300).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
            entity.HasIndex(x => new { x.ProjectId, x.CreatedAtUtc });
        });

        modelBuilder.ConfigureIdempotencyRecord();
    }
}
