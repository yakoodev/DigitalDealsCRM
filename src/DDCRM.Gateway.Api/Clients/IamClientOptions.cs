namespace DDCRM.Gateway.Api.Clients;

public sealed class IamClientOptions
{
    public const string SectionName = "IamClient";

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "http://localhost:5120";

    public string? ServiceToken { get; set; }
}
