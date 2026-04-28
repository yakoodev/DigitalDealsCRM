namespace DDCRM.AccountsManager.Api.Worker;

public sealed class WorkerControlClientOptions
{
    public const string SectionName = "WorkerControlClient";

    public bool Enabled { get; set; } = true;

    public string BaseUrlTemplate { get; set; } = "http://localhost:5072";

    public string PathPrefix { get; set; } = "/internal/v2/worker";

    public string? ServiceToken { get; set; }
}
