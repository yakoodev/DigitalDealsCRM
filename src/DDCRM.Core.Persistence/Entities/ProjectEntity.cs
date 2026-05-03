using System.ComponentModel.DataAnnotations;

namespace DDCRM.Core.Persistence.Entities;

public sealed class ProjectEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(200)]
    public required string Name { get; set; }

    [MaxLength(32)]
    public required string Status { get; set; }

    public Guid OwnerUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public List<ProjectMemberEntity> Members { get; set; } = [];

    public List<AccountEntity> Accounts { get; set; } = [];

    public List<ProjectIntegrationGrantEntity> IntegrationGrants { get; set; } = [];

    public List<ProjectServiceCredentialEntity> ServiceCredentials { get; set; } = [];

    public List<ProjectIntegrationWorkerRuntimeEntity> IntegrationWorkerRuntimes { get; set; } = [];

    public List<ProjectCustomHttpIntegrationEntity> CustomHttpIntegrations { get; set; } = [];

    public List<OfferEntity> Offers { get; set; } = [];

    public List<OfferVariantEntity> OfferVariants { get; set; } = [];

    public List<WorkflowDefinitionEntity> WorkflowDefinitions { get; set; } = [];

    public List<WorkflowTriggerEventEntity> WorkflowTriggerEvents { get; set; } = [];

    public List<WorkflowExecutionEntity> WorkflowExecutions { get; set; } = [];

    public List<TelegramChatBindingEntity> TelegramChatBindings { get; set; } = [];

    public List<TelegramLinkCodeEntity> TelegramLinkCodes { get; set; } = [];

    public List<NotificationOutboxEntity> NotificationOutboxItems { get; set; } = [];
}
