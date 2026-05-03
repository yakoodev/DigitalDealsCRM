using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class ProjectCustomHttpIntegrationEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    [MaxLength(120)]
    public required string Name { get; set; }

    [MaxLength(1000)]
    public required string BaseUrl { get; set; }

    [MaxLength(32)]
    public required string Status { get; set; }

    public required string BearerTokenCiphertext { get; set; }

    [MaxLength(24)]
    public required string BearerTokenMasked { get; set; }

    public string? DefaultHeadersJson { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? LastTestedAtUtc { get; set; }

    public ProjectEntity? Project { get; set; }
}
