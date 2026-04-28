namespace DDCRM.Core.Persistence.Entities;

public sealed class ProjectMemberEntity
{
    public Guid ProjectId { get; set; }

    public Guid UserId { get; set; }

    public required string Role { get; set; }

    public DateTimeOffset JoinedAtUtc { get; set; }

    public ProjectEntity? Project { get; set; }
}
