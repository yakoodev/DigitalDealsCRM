using System.ComponentModel.DataAnnotations;

namespace DDCRM.AccountsManager.Persistence.Entities;

public sealed class WorkerServerEntity
{
    [Key]
    [MaxLength(120)]
    public required string ServerId { get; set; }

    [MaxLength(512)]
    public required string BaseUrlTemplate { get; set; }

    [MaxLength(32)]
    public required string Status { get; set; }

    [MaxLength(32)]
    public required string Health { get; set; }

    public int Capacity { get; set; }

    public int CurrentLoad { get; set; }

    [MaxLength(4000)]
    public string? MetadataJson { get; set; }

    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
