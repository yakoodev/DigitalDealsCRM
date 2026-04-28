namespace DDCRM.Core.Api.Integrations;

public sealed class SteamAccountsManagerClientOptions
{
    public const string SectionName = "SteamAccountsManagerClient";

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "http://localhost:5200";

    public string? ServiceToken { get; set; }
}
