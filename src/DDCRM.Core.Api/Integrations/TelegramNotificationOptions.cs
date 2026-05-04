namespace DDCRM.Core.Api.Integrations;

public sealed class TelegramNotificationOptions
{
    public const string SectionName = "TelegramNotifications";

    public bool Enabled { get; set; } = true;

    public string ApiBaseUrl { get; set; } = "https://api.telegram.org";

    public string? BotToken { get; set; }

    public string? LinkWebhookSecret { get; set; }
}
