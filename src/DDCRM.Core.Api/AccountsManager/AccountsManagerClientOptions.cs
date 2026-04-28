namespace DDCRM.Core.Api.AccountsManager;

public sealed class AccountsManagerClientOptions
{
    public const string SectionName = "AccountsManagerClient";

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "http://localhost:5137";

    public string? ServiceToken { get; set; }
}
