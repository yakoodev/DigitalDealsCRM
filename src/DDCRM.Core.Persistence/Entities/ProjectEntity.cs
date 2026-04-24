using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class ProjectEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(200)]
    public required string Name { get; set; }

    [MaxLength(32)]
    public required string Status { get; set; }

    public Guid OwnerUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public List<ProjectMemberEntity> Members { get; set; } = [];

    public List<AccountEntity> Accounts { get; set; } = [];
}
