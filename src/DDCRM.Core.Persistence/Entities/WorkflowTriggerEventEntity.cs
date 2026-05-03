using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class WorkflowTriggerEventEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid OfferId { get; set; }

    [MaxLength(80)]
    public required string Source { get; set; }

    [MaxLength(160)]
    public required string SourceOrderId { get; set; }

    [MaxLength(160)]
    public string? BuyerId { get; set; }

    public required string PayloadJson { get; set; }

    [MaxLength(32)]
    public required string Status { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public ProjectEntity? Project { get; set; }

    public OfferEntity? Offer { get; set; }

    public List<WorkflowOutboxEntity> OutboxItems { get; set; } = [];

    public List<WorkflowExecutionEntity> Executions { get; set; } = [];
}
