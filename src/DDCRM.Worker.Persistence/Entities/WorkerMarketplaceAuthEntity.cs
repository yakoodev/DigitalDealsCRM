using System.ComponentModel.DataAnnotations;

namespace DDCRM.Worker.Persistence.Entities;

public sealed class WorkerMarketplaceAuthEntity
{
    [Key]
    public Guid AccountId { get; set; }

    [MaxLength(80)]
    public required string Scheme { get; set; }

    [MaxLength(8192)]
    public required string CredentialsEncrypted { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
