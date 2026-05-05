using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class WorkflowExecutionStepEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid ExecutionId { get; set; }

    [MaxLength(120)]
    public required string NodeId { get; set; }

    [MaxLength(80)]
    public required string NodeType { get; set; }

    public int StepIndex { get; set; }

    [MaxLength(32)]
    public required string Status { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }

    public string? InputJson { get; set; }

    public string? OutputJson { get; set; }

    [MaxLength(1000)]
    public string? Error { get; set; }

    public WorkflowExecutionEntity? Execution { get; set; }
}
