using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class ProjectServiceCredentialEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    [MaxLength(80)]
    public required string IntegrationKey { get; set; }

    [MaxLength(120)]
    public required string ScopesCsv { get; set; }

    [MaxLength(32)]
    public required string Status { get; set; }

    public required string SecretCiphertext { get; set; }

    [MaxLength(64)]
    public required string SecretHashSha256 { get; set; }

    [MaxLength(12)]
    public required string SecretMasked { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public ProjectEntity? Project { get; set; }
}
