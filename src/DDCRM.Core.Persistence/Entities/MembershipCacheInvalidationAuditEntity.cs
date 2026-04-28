using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class MembershipCacheInvalidationAuditEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid? ProjectId { get; set; }

    public Guid? UserId { get; set; }

    [MaxLength(200)]
    public required string Actor { get; set; }

    [MaxLength(500)]
    public required string Reason { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
