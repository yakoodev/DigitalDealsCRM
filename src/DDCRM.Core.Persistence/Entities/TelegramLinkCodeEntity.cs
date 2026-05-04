using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class TelegramLinkCodeEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    [MaxLength(64)]
    public required string Code { get; set; }

    [MaxLength(32)]
    public required string BindingType { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? ConsumedAtUtc { get; set; }

    public ProjectEntity? Project { get; set; }
}
