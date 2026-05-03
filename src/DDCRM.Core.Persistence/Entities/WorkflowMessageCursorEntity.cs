using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class WorkflowMessageCursorEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid AccountId { get; set; }

    [MaxLength(160)]
    public required string ConversationId { get; set; }

    [MaxLength(200)]
    public required string LastSeenMessageId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ProjectEntity? Project { get; set; }
}
