namespace DDCRM.Core.Api.Integrations;

public sealed class EntitlementCheckClientOptions
{
    public const string SectionName = "EntitlementCheckClient";

    public string BaseUrl { get; set; } = "http://localhost:5135";

    public string? ServiceToken { get; set; }
}
