using System.ComponentModel.DataAnnotations;

namespace DDCRM.Billing.Persistence.Entities;

public sealed class BillingAuditEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    [MaxLength(80)]
    public required string Operation { get; set; }

    [MaxLength(160)]
    public required string Actor { get; set; }

    [MaxLength(300)]
    public required string Reason { get; set; }

    public string? MetadataJson { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
