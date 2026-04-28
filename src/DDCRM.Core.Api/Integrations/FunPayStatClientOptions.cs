namespace DDCRM.Core.Api.Integrations;

public sealed class FunPayStatClientOptions
{
    public const string SectionName = "FunPayStatClient";

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "http://localhost:8000";

    public string? ServiceToken { get; set; }
}
