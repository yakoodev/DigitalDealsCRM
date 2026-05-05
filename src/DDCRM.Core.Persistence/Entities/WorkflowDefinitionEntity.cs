using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class WorkflowDefinitionEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid OfferId { get; set; }

    public required string DraftJson { get; set; }

    public string? PublishedJson { get; set; }

    [MaxLength(32)]
    public required string Status { get; set; }

    public int PublishedVersion { get; set; }

    public int MaxSteps { get; set; }

    public int MaxDurationSeconds { get; set; }

    public int MaxRetries { get; set; }

    public Guid UpdatedByUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Guid? PublishedByUserId { get; set; }

    public DateTimeOffset? PublishedAtUtc { get; set; }

    public ProjectEntity? Project { get; set; }

    public OfferEntity? Offer { get; set; }

    public List<WorkflowExecutionEntity> Executions { get; set; } = [];
}
