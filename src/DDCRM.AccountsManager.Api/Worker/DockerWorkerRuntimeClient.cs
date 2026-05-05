using System.Net.Http.Headers;
using Docker.DotNet;
using Docker.DotNet.Models;
using DDCRM.Shared.Constants;
using DDCRM.Shared.Errors;
using Microsoft.Extensions.Options;

namespace DDCRM.AccountsManager.Api.Worker;

public sealed class DockerWorkerRuntimeClient(
    IHttpClientFactory httpClientFactory,
    IOptions<AccountManagerAutospawnOptions> options)
    : IDockerWorkerRuntimeClient
{
    private readonly AccountManagerAutospawnOptions _options = options.Value;

    public bool Enabled => _options.Enabled;

    public async Task<DockerSpawnResult> SpawnWorkerAsync(
        DockerSpawnRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return new DockerSpawnResult(request.WorkerId, request.WorkerId, request.ContainerPort);
        }

        var endpoint = ResolveDockerEndpoint(request.DockerEndpointOverride);
        using var docker = new DockerClientConfiguration(new Uri(endpoint)).CreateClient();
        await EnsureImageAsync(docker, request.WorkerImage, request.RegistryAuth, cancellationToken);

        var env = request.EnvironmentVariables
            .Select(x => $"{x.Key}={x.Value}")
            .ToList();

        var containerName = NormalizeContainerName(request.WorkerId);
        var dockerNetwork = ResolveDockerNetwork(request.DockerNetworkOverride);
        var command = request.WorkerCommand is { Count: > 0 }
            ? request.WorkerCommand
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToArray()
            : null;

        var createParams = new CreateContainerParameters
        {
            Image = request.WorkerImage,
            Name = containerName,
            Cmd = command,
            Env = env,
            ExposedPorts = new Dictionary<string, EmptyStruct>
            {
                [$"{request.ContainerPort}/tcp"] = default,
            },
            HostConfig = new HostConfig
            {
                NetworkMode = dockerNetwork,
                RestartPolicy = new RestartPolicy
                {
                    Name = RestartPolicyKind.UnlessStopped,
                },
            },
        };

        CreateContainerResponse created;
        try
        {
            created = await docker.Containers.CreateContainerAsync(createParams, cancellationToken);
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            await RemoveWorkerAsync(new DockerRemoveRequest(containerName, endpoint), cancellationToken);
            created = await docker.Containers.CreateContainerAsync(createParams, cancellationToken);
        }

        var started = await docker.Containers.StartContainerAsync(
            created.ID,
            new ContainerStartParameters(),
            cancellationToken);

        if (!started)
        {
            throw new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                $"Docker autospawn не смог запустить контейнер `{containerName}`.");
        }

        try
        {
            await WaitForHealthAsync(
                containerName,
                request.ContainerPort,
                request.HealthPathOverride,
                cancellationToken);
        }
        catch
        {
            try
            {
                await RemoveWorkerAsync(new DockerRemoveRequest(containerName, endpoint), CancellationToken.None);
            }
            catch
            {
                // no-op; keep original health error
            }

            throw;
        }

        return new DockerSpawnResult(
            containerName,
            created.ID.Length >= 12 ? created.ID[..12] : created.ID,
            request.ContainerPort);
    }

    public async Task RemoveWorkerAsync(
        DockerRemoveRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return;
        }

        var endpoint = ResolveDockerEndpoint(request.DockerEndpointOverride);
        using var docker = new DockerClientConfiguration(new Uri(endpoint)).CreateClient();
        var containerName = NormalizeContainerName(request.WorkerId);

        IList<ContainerListResponse> containers;
        try
        {
            containers = await docker.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool>(StringComparer.Ordinal)
                    {
                        [containerName] = true,
                    },
                },
            }, cancellationToken);
        }
        catch
        {
            return;
        }

        var target = containers.FirstOrDefault(x =>
            x.Names.Any(name => string.Equals(name.TrimStart('/'), containerName, StringComparison.Ordinal)));
        if (target is null)
        {
            return;
        }

        try
        {
            if (string.Equals(target.State, "running", StringComparison.OrdinalIgnoreCase))
            {
                await docker.Containers.StopContainerAsync(
                    target.ID,
                    new ContainerStopParameters
                    {
                        WaitBeforeKillSeconds = 5,
                    },
                    cancellationToken);
            }
        }
        catch
        {
            // no-op; remove attempt below
        }

        try
        {
            await docker.Containers.RemoveContainerAsync(
                target.ID,
                new ContainerRemoveParameters
                {
                    Force = true,
                    RemoveVolumes = true,
                },
                cancellationToken);
        }
        catch
        {
            // no-op
        }
    }

    private async Task EnsureImageAsync(
        DockerClient docker,
        string image,
        DockerRegistryAuthConfig? registryAuth,
        CancellationToken cancellationToken)
    {
        var imageName = image.Trim();
        if (string.IsNullOrWhiteSpace(imageName))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "Worker template должен содержать непустой runtime.workerImage.");
        }

        if (await HasImageAsync(docker, imageName, cancellationToken))
        {
            return;
        }

        var authConfig = BuildPullAuthConfig(imageName, registryAuth);
        var pullErrors = new List<string>();
        try
        {
            await docker.Images.CreateImageAsync(
                new ImagesCreateParameters
                {
                    FromImage = imageName,
                },
                authConfig,
                new Progress<JSONMessage>(message =>
                {
                    if (!string.IsNullOrWhiteSpace(message.ErrorMessage))
                    {
                        pullErrors.Add(message.ErrorMessage);
                    }
                }),
                cancellationToken);
        }
        catch (DockerApiException ex)
        {
            var details = pullErrors.Count > 0
                ? pullErrors[0]
                : ex.Message;
            throw new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                $"Не удалось выполнить pull Docker image `{imageName}`. {details}");
        }

        if (pullErrors.Count > 0)
        {
            throw new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                $"Не удалось выполнить pull Docker image `{imageName}`. {pullErrors[0]}");
        }

        if (await HasImageAsync(docker, imageName, cancellationToken))
        {
            return;
        }

        throw new ApiErrorException(
            StatusCodes.Status502BadGateway,
            ApiErrorCodes.InternalError,
            $"Docker image `{imageName}` недоступна после pull-if-missing.");
    }

    private static AuthConfig? BuildPullAuthConfig(string imageName, DockerRegistryAuthConfig? registryAuth)
    {
        if (!IsGhcrImage(imageName))
        {
            return null;
        }

        if (registryAuth is null || !registryAuth.Enabled)
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "Для образов ghcr.io требуется registry-конфиг с активным token на worker-server.");
        }

        var host = registryAuth.Host?.Trim();
        if (!string.Equals(host, "ghcr.io", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "В v1 поддерживается только registry host `ghcr.io`.");
        }

        var username = registryAuth.Username?.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "Для образов ghcr.io требуется registry username на worker-server.");
        }

        var token = registryAuth.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "Для образов ghcr.io требуется registry token на worker-server.");
        }

        return new AuthConfig
        {
            ServerAddress = "ghcr.io",
            Username = username,
            Password = token,
        };
    }

    private static bool IsGhcrImage(string imageName)
    {
        return imageName.StartsWith("ghcr.io/", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> HasImageAsync(
        DockerClient docker,
        string imageName,
        CancellationToken cancellationToken)
    {
        var existing = await docker.Images.ListImagesAsync(
            new ImagesListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>(StringComparer.Ordinal)
                {
                    ["reference"] = new Dictionary<string, bool>(StringComparer.Ordinal)
                    {
                        [imageName] = true,
                    },
                },
            },
            cancellationToken);

        return existing.Count > 0;
    }

    private async Task WaitForHealthAsync(
        string workerId,
        int containerPort,
        string? healthPathOverride,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(5, _options.HealthTimeoutSeconds));
        var interval = TimeSpan.FromMilliseconds(Math.Max(200, _options.HealthPollIntervalMilliseconds));
        var healthPath = string.IsNullOrWhiteSpace(healthPathOverride)
            ? "/health"
            : healthPathOverride;
        if (!healthPath.StartsWith('/'))
        {
            healthPath = $"/{healthPath}";
        }

        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var client = httpClientFactory.CreateClient("docker-autospawn-health");
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"http://{workerId}:{containerPort}{healthPath}");
                if (!string.IsNullOrWhiteSpace(_options.WorkerApiServiceToken))
                {
                    request.Headers.TryAddWithoutValidation(HeaderNames.ServiceToken, _options.WorkerApiServiceToken);
                }

                using var response = await client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // wait and retry
            }

            await Task.Delay(interval, cancellationToken);
        }

        throw new ApiErrorException(
            StatusCodes.Status504GatewayTimeout,
            ApiErrorCodes.InternalError,
            $"Worker `{workerId}` не прошёл health-check после autospawn.");
    }

    private string ResolveDockerEndpoint(string? endpointOverride)
    {
        var endpoint = endpointOverride;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint = _options.DockerEndpoint;
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ApiErrorException(
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.InternalError,
                "Не задан Docker endpoint для account manager autospawn.");
        }

        return endpoint.Trim();
    }

    private string ResolveDockerNetwork(string? networkOverride)
    {
        var network = string.IsNullOrWhiteSpace(networkOverride)
            ? _options.DockerNetwork
            : networkOverride;
        if (string.IsNullOrWhiteSpace(network))
        {
            throw new ApiErrorException(
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.InternalError,
                "Не задан Docker network для account manager autospawn.");
        }

        return network.Trim();
    }

    private static string NormalizeContainerName(string workerId)
    {
        var cleaned = workerId.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "workerId для autospawn не может быть пустым.");
        }

        cleaned = new string(cleaned
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')
            .ToArray());

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            throw new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "workerId содержит только недопустимые символы.");
        }

        return cleaned.Length > 63
            ? cleaned[..63]
            : cleaned;
    }
}
