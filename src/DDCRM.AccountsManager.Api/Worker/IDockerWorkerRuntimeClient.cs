namespace DDCRM.AccountsManager.Api.Worker;

public interface IDockerWorkerRuntimeClient
{
    bool Enabled { get; }

    Task<DockerSpawnResult> SpawnWorkerAsync(
        DockerSpawnRequest request,
        CancellationToken cancellationToken);

    Task RemoveWorkerAsync(
        DockerRemoveRequest request,
        CancellationToken cancellationToken);
}

public sealed record DockerSpawnRequest(
    string WorkerId,
    string Platform,
    string WorkerImage,
    int ContainerPort,
    string? DockerNetworkOverride,
    string? DockerEndpointOverride,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    string? HealthPathOverride,
    DockerRegistryAuthConfig? RegistryAuth);

public sealed record DockerSpawnResult(
    string WorkerId,
    string PodId,
    int ContainerPort);

public sealed record DockerRemoveRequest(
    string WorkerId,
    string? DockerEndpointOverride);

public sealed record DockerRegistryAuthConfig(
    bool Enabled,
    string Host,
    string? Username,
    string? Token);
