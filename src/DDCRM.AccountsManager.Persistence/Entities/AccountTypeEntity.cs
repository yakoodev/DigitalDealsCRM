using System.ComponentModel.DataAnnotations;

namespace DDCRM.AccountsManager.Persistence.Entities;

public sealed class AccountTypeEntity
{
    [Key]
    [MaxLength(120)]
    public required string AccountTypeId { get; set; }

    [MaxLength(80)]
    public required string Platform { get; set; }

    [MaxLength(160)]
    public required string DisplayName { get; set; }

    [MaxLength(400)]
    public string? Description { get; set; }

    [MaxLength(80)]
    public required string WorkerProfileId { get; set; }

    public bool Enabled { get; set; }

    public int SortOrder { get; set; }

    [MaxLength(8000)]
    public string? FormFieldsJson { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
