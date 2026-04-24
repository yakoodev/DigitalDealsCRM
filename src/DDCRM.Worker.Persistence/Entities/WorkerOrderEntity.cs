using System.ComponentModel.DataAnnotations;

namespace DDCRM.Worker.Persistence.Entities;

public sealed class WorkerOrderEntity
{
    [Key]
    [MaxLength(120)]
    public required string Id { get; set; }

    [MaxLength(80)]
    public required string Status { get; set; }

    public decimal Total { get; set; }

    [MaxLength(8)]
    public required string Currency { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
