namespace DDCRM.Gateway.Api.Clients;

public sealed class EntitlementClientOptions
{
    public const string SectionName = "EntitlementClient";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://localhost:5130";

    public string? ServiceToken { get; set; }
}
