namespace DDCRM.AccountsManager.Api.Worker;

public sealed class AccountManagerAutospawnOptions
{
    public bool Enabled { get; set; } = false;

    public string DockerEndpoint { get; set; } = "unix:///var/run/docker.sock";

    public string DockerNetwork { get; set; } = "ddcrm_ddcrm";

    public int WorkerInternalPort { get; set; } = 8080;

    public int HealthTimeoutSeconds { get; set; } = 45;

    public int HealthPollIntervalMilliseconds { get; set; } = 800;

    public string FallbackWorkerId { get; set; } = "worker-api";

    public string? WorkerApiServiceToken { get; set; }
}
