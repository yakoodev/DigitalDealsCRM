using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class ProjectIntegrationGrantEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    [MaxLength(80)]
    public required string IntegrationKey { get; set; }

    [MaxLength(32)]
    public required string Status { get; set; }

    [MaxLength(120)]
    public required string ScopesCsv { get; set; }

    public int MaxInstances { get; set; } = 1;

    public Guid GrantedByUserId { get; set; }

    public DateTimeOffset GrantedAtUtc { get; set; }

    public Guid? RevokedByUserId { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public ProjectEntity? Project { get; set; }
}
