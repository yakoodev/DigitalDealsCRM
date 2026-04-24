using System.ComponentModel.DataAnnotations;

namespace DDCRM.Entitlement.Persistence.Entities;

public sealed class EntitlementOverrideEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    [MaxLength(160)]
    public required string Actor { get; set; }

    [MaxLength(300)]
    public required string Reason { get; set; }

    [MaxLength(32)]
    public required string Status { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public required string DataJson { get; set; }
}
