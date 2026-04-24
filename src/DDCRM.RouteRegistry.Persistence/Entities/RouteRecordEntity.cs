using System.ComponentModel.DataAnnotations;

namespace DDCRM.RouteRegistry.Persistence.Entities;

public sealed class RouteRecordEntity
{
    [Key]
    public Guid AccountId { get; set; }

    public Guid ProjectId { get; set; }

    [MaxLength(150)]
    public required string RouteKey { get; set; }

    [MaxLength(120)]
    public required string ServerId { get; set; }

    [MaxLength(120)]
    public required string WorkerId { get; set; }

    [MaxLength(120)]
    public string? PodId { get; set; }

    public int RouteVersion { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
