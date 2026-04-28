using DDCRM.Shared.Idempotency;
using DDCRM.Worker.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DDCRM.Worker.Persistence;

public sealed class WorkerDbContext(DbContextOptions<WorkerDbContext> options)
    : DbContext(options), IIdempotencyDbContext
{
    public DbSet<WorkerListingEntity> Listings => Set<WorkerListingEntity>();

    public DbSet<WorkerOrderEntity> Orders => Set<WorkerOrderEntity>();

    public DbSet<WorkerProxyCredentialsEntity> ProxyCredentials => Set<WorkerProxyCredentialsEntity>();

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

        modelBuilder.Entity<WorkerProxyCredentialsEntity>(entity =>
        {
            entity.ToTable("worker_proxy_credentials");
            entity.HasKey(x => x.AccountId);
            entity.Property(x => x.Host).HasMaxLength(255).IsRequired();
            entity.Property(x => x.Port).IsRequired();
            entity.Property(x => x.Login).HasMaxLength(255).IsRequired();
            entity.Property(x => x.Password).HasMaxLength(512).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("NOW()");
        });

        modelBuilder.ConfigureIdempotencyRecord();
    }
}
