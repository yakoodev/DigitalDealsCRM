namespace DDCRM.Core.Api.GatewayProxy;

public sealed class GatewayProxyClientOptions
{
    public const string SectionName = "GatewayProxyClient";

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "http://localhost:5068";
}
