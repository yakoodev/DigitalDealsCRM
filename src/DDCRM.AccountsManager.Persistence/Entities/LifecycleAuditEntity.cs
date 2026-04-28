using System.ComponentModel.DataAnnotations;

namespace DDCRM.AccountsManager.Persistence.Entities;

public sealed class LifecycleAuditEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid ProjectId { get; set; }

    [MaxLength(64)]
    public required string Operation { get; set; }

    [MaxLength(160)]
    public required string Actor { get; set; }

    [MaxLength(400)]
    public required string Notes { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
