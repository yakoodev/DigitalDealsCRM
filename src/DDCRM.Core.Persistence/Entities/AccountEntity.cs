using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class AccountEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    [MaxLength(80)]
    public required string Platform { get; set; }

    [MaxLength(200)]
    public required string DisplayName { get; set; }

    [MaxLength(64)]
    public required string BusinessStatus { get; set; }

    public bool ProxyConfigured { get; set; }

    [MaxLength(255)]
    public string? ProxyHostMasked { get; set; }

    [MaxLength(255)]
    public string? ProxyLoginMasked { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public ProjectEntity? Project { get; set; }
}
