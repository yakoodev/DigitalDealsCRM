using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class OfferEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    [MaxLength(200)]
    public required string Name { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(32)]
    public required string Status { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ProjectEntity? Project { get; set; }

    public List<OfferVariantEntity> Variants { get; set; } = [];

    public List<WorkflowDefinitionEntity> WorkflowDefinitions { get; set; } = [];

    public List<WorkflowTriggerEventEntity> WorkflowTriggerEvents { get; set; } = [];

    public List<WorkflowExecutionEntity> WorkflowExecutions { get; set; } = [];
}
