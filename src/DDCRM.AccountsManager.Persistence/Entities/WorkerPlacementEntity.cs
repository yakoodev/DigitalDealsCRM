using System.ComponentModel.DataAnnotations;

namespace DDCRM.AccountsManager.Persistence.Entities;

public sealed class WorkerPlacementEntity
{
    [Key]
    public Guid AccountId { get; set; }

    public Guid ProjectId { get; set; }

    [MaxLength(80)]
    public required string Platform { get; set; }

    [MaxLength(200)]
    public required string WorkerId { get; set; }

    [MaxLength(120)]
    public required string ServerId { get; set; }

    [MaxLength(120)]
    public string? PodId { get; set; }

    [MaxLength(32)]
    public required string LifecycleStatus { get; set; }

    public bool ProxyConfigured { get; set; }

    public int RouteVersion { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
