using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class OfferVariantEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid OfferId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid AccountId { get; set; }

    [MaxLength(200)]
    public required string WorkerProductId { get; set; }

    [MaxLength(80)]
    public required string Platform { get; set; }

    [MaxLength(400)]
    public required string ObservedTitle { get; set; }

    [MaxLength(4000)]
    public string? ObservedDescription { get; set; }

    public decimal ObservedPrice { get; set; }

    [MaxLength(8)]
    public required string ObservedCurrency { get; set; }

    public int Priority { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public OfferEntity? Offer { get; set; }
}
