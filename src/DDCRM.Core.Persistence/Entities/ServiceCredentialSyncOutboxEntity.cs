using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class ServiceCredentialSyncOutboxEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid CredentialId { get; set; }

    [MaxLength(80)]
    public required string IntegrationKey { get; set; }

    [MaxLength(32)]
    public required string Operation { get; set; }

    [MaxLength(32)]
    public required string Status { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset NextAttemptAtUtc { get; set; }

    [MaxLength(1000)]
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }
}
