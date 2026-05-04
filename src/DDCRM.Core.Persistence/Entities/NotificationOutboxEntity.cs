using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class NotificationOutboxEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    [MaxLength(32)]
    public required string Channel { get; set; }

    [MaxLength(120)]
    public required string EventType { get; set; }

    [MaxLength(160)]
    public required string DeduplicationKey { get; set; }

    [MaxLength(32)]
    public required string Status { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset NextAttemptAtUtc { get; set; }

    [MaxLength(1000)]
    public string? LastError { get; set; }

    public string PayloadJson { get; set; } = "{}";

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public ProjectEntity? Project { get; set; }
}
