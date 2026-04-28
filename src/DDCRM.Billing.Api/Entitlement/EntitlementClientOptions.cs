namespace DDCRM.Billing.Api.Entitlement;

public sealed class EntitlementClientOptions
{
    public const string SectionName = "EntitlementClient";

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "http://localhost:5221";

    public string? ServiceToken { get; set; }
}
