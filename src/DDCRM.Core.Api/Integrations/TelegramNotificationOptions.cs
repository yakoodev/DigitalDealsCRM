namespace DDCRM.Core.Api.Integrations;

public sealed class TelegramNotificationOptions
{
    public const string SectionName = "TelegramNotifications";

    public bool Enabled { get; set; } = true;

    public string ApiBaseUrl { get; set; } = "https://api.telegram.org";

    public string? BotToken { get; set; }

    public string? LinkWebhookSecret { get; set; }

    public bool FallbackToDirectOnProxyFailure { get; set; } = true;

    public int RequestTimeoutSeconds { get; set; } = 25;

    public bool PollingEnabled { get; set; } = true;

    public int PollingIntervalSeconds { get; set; } = 2;

    public int PollingTimeoutSeconds { get; set; } = 25;

    public int PollingBatchSize { get; set; } = 30;
}
