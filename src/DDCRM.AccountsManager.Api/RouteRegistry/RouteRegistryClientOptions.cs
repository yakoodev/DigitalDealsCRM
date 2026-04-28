namespace DDCRM.AccountsManager.Api.RouteRegistry;

public sealed class RouteRegistryClientOptions
{
    public bool Enabled { get; set; }

    public string? BaseUrl { get; set; }

    public string? ServiceToken { get; set; }
}
