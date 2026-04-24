using System.ComponentModel.DataAnnotations;

namespace DDCRM.Billing.Persistence.Entities;

public sealed class BillingSubscriptionEntity
{
    [Key]
    public Guid ProjectId { get; set; }

    [MaxLength(80)]
    public required string Status { get; set; }

    [MaxLength(120)]
    public required string PlanKey { get; set; }

    public DateTimeOffset? TrialEndsAtUtc { get; set; }

    public DateTimeOffset? GraceEndsAtUtc { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
