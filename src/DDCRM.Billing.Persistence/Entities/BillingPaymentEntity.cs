using System.ComponentModel.DataAnnotations;

namespace DDCRM.Billing.Persistence.Entities;

public sealed class BillingPaymentEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    [MaxLength(80)]
    public required string Status { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(8)]
    public required string Currency { get; set; }

    [MaxLength(120)]
    public required string ProviderPaymentId { get; set; }

    [MaxLength(120)]
    public string? PlanKey { get; set; }

    [MaxLength(64)]
    public required string SourceOperation { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
