using System.ComponentModel.DataAnnotations;

namespace DDCRM.Billing.Persistence.Entities;

public sealed class BillingRefundEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid PaymentId { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(80)]
    public required string Status { get; set; }

    [MaxLength(200)]
    public string? Reason { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
