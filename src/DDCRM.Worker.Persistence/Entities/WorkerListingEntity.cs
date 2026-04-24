using System.ComponentModel.DataAnnotations;

namespace DDCRM.Worker.Persistence.Entities;

public sealed class WorkerListingEntity
{
    [Key]
    [MaxLength(120)]
    public required string Id { get; set; }

    [MaxLength(300)]
    public required string Title { get; set; }

    [MaxLength(80)]
    public required string Status { get; set; }

    public decimal Price { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
