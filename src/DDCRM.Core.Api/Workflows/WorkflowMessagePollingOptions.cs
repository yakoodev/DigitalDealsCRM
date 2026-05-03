namespace DDCRM.Core.Api.Workflows;

public sealed class WorkflowMessagePollingOptions
{
    public const string SectionName = "WorkflowMessagePolling";

    public bool Enabled { get; set; } = true;

    public string RouteRegistryBaseUrl { get; set; } = "http://localhost:15110";

    public string WorkerBaseUrlTemplate { get; set; } = "http://{workerId}:8080";

    public string WorkerPathPrefix { get; set; } = "/internal/v2/worker";

    public bool OnlyUnreadConversations { get; set; } = true;

    public bool DispatchWorkerReplies { get; set; } = true;

    public string? InternalServiceToken { get; set; }

    public string? WorkerServiceToken { get; set; }

    public int PollIntervalSeconds { get; set; } = 5;

    public int ConversationLimit { get; set; } = 20;

    public int MessagesLimit { get; set; } = 20;

    public int MaxConversationsPerAccount { get; set; } = 10;

    public int MaxMessagesPerConversationPerPoll { get; set; } = 1;

    public bool SkipFirstMessagePerConversation { get; set; } = true;

    public int MaxMessageAgeMinutes { get; set; } = 30;
}
