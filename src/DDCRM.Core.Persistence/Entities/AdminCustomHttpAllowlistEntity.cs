using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class AdminCustomHttpAllowlistEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(255)]
    public required string HostPattern { get; set; }

    public bool IsActive { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Guid UpdatedByUserId { get; set; }
}
