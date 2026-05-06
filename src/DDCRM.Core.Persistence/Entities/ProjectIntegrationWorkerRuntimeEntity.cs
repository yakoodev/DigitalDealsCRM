using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class ProjectIntegrationWorkerRuntimeEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    [MaxLength(80)]
    public required string IntegrationKey { get; set; }

    [MaxLength(160)]
    public required string InstanceDisplayName { get; set; }

    public bool IsDefault { get; set; }

    public Guid RuntimeAccountId { get; set; }

    [MaxLength(32)]
    public required string Status { get; set; }

    [MaxLength(1000)]
    public string? LastError { get; set; }

    public string? ConfigurationCiphertext { get; set; }

    public DateTimeOffset? ConfigurationUpdatedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? ProvisionedAtUtc { get; set; }

    public DateTimeOffset? DeprovisionedAtUtc { get; set; }

    public ProjectEntity? Project { get; set; }
}
