using Microsoft.EntityFrameworkCore;

namespace DDCRM.Shared.Idempotency;

public static class ModelBuilderExtensions
{
    public static void ConfigureIdempotencyRecord(this ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<IdempotencyRecord>();
        entity.ToTable("idempotency_records");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Scope).HasMaxLength(160).IsRequired();
        entity.Property(x => x.Key).HasMaxLength(200).IsRequired();
        entity.Property(x => x.PayloadJson).IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");
        entity.HasIndex(x => new { x.Scope, x.Key }).IsUnique();
    }
}
