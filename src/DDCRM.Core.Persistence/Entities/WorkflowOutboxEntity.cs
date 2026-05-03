using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class WorkflowOutboxEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid TriggerEventId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid OfferId { get; set; }

    [MaxLength(32)]
    public required string Status { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset NextAttemptAtUtc { get; set; }

    [MaxLength(1000)]
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public WorkflowTriggerEventEntity? TriggerEvent { get; set; }
}
