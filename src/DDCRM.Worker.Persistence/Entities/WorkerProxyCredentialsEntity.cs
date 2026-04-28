using System.ComponentModel.DataAnnotations;

namespace DDCRM.Worker.Persistence.Entities;

public sealed class WorkerProxyCredentialsEntity
{
    [Key]
    public Guid AccountId { get; set; }

    [MaxLength(255)]
    public required string Host { get; set; }

    public int Port { get; set; }

    [MaxLength(255)]
    public required string Login { get; set; }

    [MaxLength(512)]
    public required string Password { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
