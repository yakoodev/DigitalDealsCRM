using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class ProxyCredentialsAuditEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid AccountId { get; set; }

    public Guid ActorUserId { get; set; }

    [MaxLength(32)]
    public required string Operation { get; set; }

    [MaxLength(500)]
    public required string Reason { get; set; }

    [MaxLength(128)]
    public required string RequestId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
