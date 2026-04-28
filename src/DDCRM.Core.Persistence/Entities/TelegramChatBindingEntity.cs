using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class TelegramChatBindingEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    [MaxLength(120)]
    public required string ChatId { get; set; }

    [MaxLength(32)]
    public required string BindingType { get; set; }

    public Guid? UserId { get; set; }

    [MaxLength(200)]
    public string? ChatTitle { get; set; }

    public Guid? LinkedByUserId { get; set; }

    public DateTimeOffset LinkedAtUtc { get; set; }

    public ProjectEntity? Project { get; set; }
}
