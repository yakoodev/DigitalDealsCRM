using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class AuthExternalIdentityEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    [MaxLength(64)]
    public required string Provider { get; set; }

    [MaxLength(256)]
    public required string ProviderUserId { get; set; }

    [MaxLength(320)]
    public string? ProviderEmail { get; set; }

    [MaxLength(4000)]
    public string? MetadataJson { get; set; }

    public DateTimeOffset LinkedAtUtc { get; set; }

    public DateTimeOffset? LastUsedAtUtc { get; set; }

    public AuthUserEntity? User { get; set; }
}
