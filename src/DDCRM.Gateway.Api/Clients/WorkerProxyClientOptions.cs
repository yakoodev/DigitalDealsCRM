namespace DDCRM.Gateway.Api.Clients;

public sealed class WorkerProxyClientOptions
{
    public const string SectionName = "WorkerProxyClient";

    public bool Enabled { get; set; } = true;

    public string BaseUrlTemplate { get; set; } = "http://localhost:5140";

    public string PathPrefix { get; set; } = "/internal/v2/worker";

    public string? ServiceToken { get; set; }
}
