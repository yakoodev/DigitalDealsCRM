namespace DDCRM.Core.Api.Integrations;

public static class IntegrationKeys
{
    public const string FunPayStat = "funpaystat";
    public const string SteamAccountsManager = "steam-accounts-manager";
    public const string Telegram = "telegram";

    public static readonly HashSet<string> ServiceIntegrations =
    [
        FunPayStat,
        SteamAccountsManager,
    ];

    public static readonly HashSet<string> All =
    [
        FunPayStat,
        SteamAccountsManager,
        Telegram,
    ];
}
