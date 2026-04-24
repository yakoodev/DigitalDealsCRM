using System.ComponentModel.DataAnnotations;

namespace DDCRM.Entitlement.Persistence.Entities;

public sealed class EntitlementAuditEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    [MaxLength(80)]
    public required string Operation { get; set; }

    [MaxLength(160)]
    public required string Actor { get; set; }

    [MaxLength(400)]
    public required string Details { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
