using System.ComponentModel.DataAnnotations;

namespace DDCRM.Entitlement.Persistence.Entities;

public sealed class EntitlementSnapshotEntity
{
    [Key]
    public Guid ProjectId { get; set; }

    [MaxLength(32)]
    public required string State { get; set; }

    public required string DataJson { get; set; }

    public DateTimeOffset CalculatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
