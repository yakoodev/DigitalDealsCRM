namespace DDCRM.Core.Api.Integrations;

public static class IntegrationKeys
{
    public const string FunPayStat = "funpaystat";
    public const string SteamAccountsManager = "steam-accounts-manager";
    public const string Telegram = "telegram";
    public const string CustomHttp = "custom-http";

    public static readonly HashSet<string> ServiceIntegrations =
    [
        FunPayStat,
    ];

    public static readonly HashSet<string> WorkerIntegrations =
    [
        SteamAccountsManager,
    ];

    public static readonly HashSet<string> NotificationIntegrations =
    [
        Telegram,
    ];

    public static readonly HashSet<string> All =
    [
        FunPayStat,
        SteamAccountsManager,
        Telegram,
        CustomHttp,
    ];

    public static string ResolveIntegrationType(string integrationKey)
    {
        if (ServiceIntegrations.Contains(integrationKey))
        {
            return "service";
        }

        if (WorkerIntegrations.Contains(integrationKey))
        {
            return "worker";
        }

        if (NotificationIntegrations.Contains(integrationKey))
        {
            return "notification";
        }

        if (string.Equals(integrationKey, CustomHttp, StringComparison.OrdinalIgnoreCase))
        {
            return "custom";
        }

        if (integrationKey.StartsWith("platform.", StringComparison.Ordinal))
        {
            return "platform";
        }

        return "unknown";
    }
}
