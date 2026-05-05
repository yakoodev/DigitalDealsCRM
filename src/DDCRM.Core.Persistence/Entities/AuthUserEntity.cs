using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class AuthUserEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(320)]
    public required string Email { get; set; }

    [MaxLength(320)]
    public required string EmailNormalized { get; set; }

    [MaxLength(160)]
    public required string DisplayName { get; set; }

    [MaxLength(1024)]
    public required string PasswordHash { get; set; }

    [MaxLength(512)]
    public string? SystemPermissionsCsv { get; set; }

    public bool ForcePasswordChange { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? LastLoginAtUtc { get; set; }

    public List<AuthExternalIdentityEntity> ExternalIdentities { get; set; } = [];
}
