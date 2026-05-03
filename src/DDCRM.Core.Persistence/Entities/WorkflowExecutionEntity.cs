using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class WorkflowExecutionEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid OfferId { get; set; }

    public Guid WorkflowDefinitionId { get; set; }

    public Guid TriggerEventId { get; set; }

    [MaxLength(160)]
    public required string SourceOrderId { get; set; }

    public int WorkflowVersion { get; set; }

    [MaxLength(32)]
    public required string Status { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }

    [MaxLength(1000)]
    public string? LastError { get; set; }

    public string? OutputJson { get; set; }

    public ProjectEntity? Project { get; set; }

    public OfferEntity? Offer { get; set; }

    public WorkflowDefinitionEntity? WorkflowDefinition { get; set; }

    public WorkflowTriggerEventEntity? TriggerEvent { get; set; }

    public List<WorkflowExecutionStepEntity> Steps { get; set; } = [];
}
