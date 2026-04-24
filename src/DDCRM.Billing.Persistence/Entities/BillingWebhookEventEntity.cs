using System.ComponentModel.DataAnnotations;

namespace DDCRM.Billing.Persistence.Entities;

public sealed class BillingWebhookEventEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(160)]
    public required string EventId { get; set; }

    [MaxLength(120)]
    public required string EventType { get; set; }

    public Guid? ProjectId { get; set; }

    public Guid? PaymentId { get; set; }

    public required string PayloadJson { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ProcessedAtUtc { get; set; }
}
