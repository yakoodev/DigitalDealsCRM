namespace DDCRM.Gateway.Api.Clients;

public sealed class RouteRegistryClientOptions
{
    public const string SectionName = "RouteRegistryClient";

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "http://localhost:5110";

    public string? ServiceToken { get; set; }
}
