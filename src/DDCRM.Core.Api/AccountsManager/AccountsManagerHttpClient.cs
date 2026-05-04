using System.Net;
using System.Net.Http.Json;
using DDCRM.Shared.Constants;
using DDCRM.Shared.Errors;
using Microsoft.Extensions.Options;

namespace DDCRM.Core.Api.AccountsManager;

public sealed class AccountsManagerHttpClient(
    HttpClient httpClient,
    IOptions<AccountsManagerClientOptions> options)
    : IAccountsManagerClient
{
    private readonly AccountsManagerClientOptions _options = options.Value;

    public async Task<IReadOnlyList<AccountsManagerWorkerServerDefinition>> ListWorkerServersAsync(
        CancellationToken cancellationToken)
    {
        EnsureEnabled();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/internal/v1/worker-servers");
        ApplyHeaders(request);

        var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw CreateUpstreamError(response.StatusCode, body, "listWorkerServers");
        }

        var payload = await response.Content.ReadFromJsonAsync<InternalWorkerServerListResponse>(cancellationToken);
        if (payload?.Items is null)
        {
            return [];
        }

        return payload.Items.Select(ToDefinition).ToList();
    }

    public async Task<AccountsManagerWorkerServerDefinition> UpsertWorkerServerAsync(
        string serverId,
        AccountsManagerWorkerServerUpsertInput input,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/internal/v1/worker-servers/{Uri.EscapeDataString(serverId)}")
        {
            Content = JsonContent.Create(input),
        };
        ApplyHeaders(request, idempotencyKey);

        var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw CreateUpstreamError(response.StatusCode, body, "upsertWorkerServer");
        }

        var payload = await response.Content.ReadFromJsonAsync<InternalWorkerServerResponse>(cancellationToken);
        if (payload?.WorkerServer is null)
        {
            throw new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                "Accounts Manager upsertWorkerServer вернул пустой payload.");
        }

        return ToDefinition(payload.WorkerServer);
    }

    public async Task<IReadOnlyList<AccountsManagerAccountTypeDefinition>> ListAccountTypesAsync(
        CancellationToken cancellationToken)
    {
        EnsureEnabled();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/internal/v1/account-types");
        ApplyHeaders(request);

        var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw CreateUpstreamError(response.StatusCode, body, "listAccountTypes");
        }

        var payload = await response.Content.ReadFromJsonAsync<InternalAccountTypeListResponse>(cancellationToken);
        if (payload?.Items is null)
        {
            return [];
        }

        return payload.Items.Select(ToDefinition).ToList();
    }

    public async Task<AccountsManagerAccountTypeDefinition> UpsertAccountTypeAsync(
        string accountTypeId,
        AccountsManagerAccountTypeUpsertInput input,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/internal/v1/account-types/{Uri.EscapeDataString(accountTypeId)}")
        {
            Content = JsonContent.Create(input),
        };
        ApplyHeaders(request, idempotencyKey);

        var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw CreateUpstreamError(response.StatusCode, body, "upsertAccountType");
        }

        var payload = await response.Content.ReadFromJsonAsync<InternalAccountTypeResponse>(cancellationToken);
        if (payload?.AccountType is null)
        {
            throw new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                "Accounts Manager upsertAccountType вернул пустой payload.");
        }

        return ToDefinition(payload.AccountType);
    }

    public async Task CreateLifecycleAsync(
        Guid projectId,
        Guid accountId,
        string platform,
        IDictionary<string, object?> proxyConfig,
        AccountsManagerMarketplaceAuth? marketplaceAuth,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/lifecycle/create")
        {
            Content = JsonContent.Create(new
            {
                accountId,
                projectId,
                platform,
                proxyConfig,
                marketplaceAuth,
            }),
        };

        ApplyHeaders(request, idempotencyKey);
        await EnsureSuccessAsync(request, "create", cancellationToken);
    }

    public async Task UpdateLifecycleAsync(
        Guid accountId,
        IDictionary<string, object?> proxyConfig,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/lifecycle/update")
        {
            Content = JsonContent.Create(new
            {
                accountId,
                proxyConfig,
            }),
        };

        ApplyHeaders(request, idempotencyKey);
        await EnsureSuccessAsync(request, "update", cancellationToken);
    }

    public async Task DeleteLifecycleAsync(
        Guid accountId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/lifecycle/delete")
        {
            Content = JsonContent.Create(new
            {
                accountId,
            }),
        };

        ApplyHeaders(request, idempotencyKey);
        await EnsureSuccessAsync(request, "delete", cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpRequestMessage request, string operation, CancellationToken cancellationToken)
    {
        var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw CreateUpstreamError(response.StatusCode, body, operation);
    }

    private void EnsureEnabled()
    {
        if (_options.Enabled)
        {
            return;
        }

        throw new ApiErrorException(
            StatusCodes.Status503ServiceUnavailable,
            ApiErrorCodes.InternalError,
            "Accounts Manager client отключен в текущем runtime-профиле.");
    }

    private void ApplyHeaders(HttpRequestMessage request, string? idempotencyKey = null)
    {
        if (!string.IsNullOrWhiteSpace(_options.ServiceToken))
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.ServiceToken, _options.ServiceToken);
        }

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.IdempotencyKey, idempotencyKey);
        }
    }

    private static ApiErrorException CreateUpstreamError(HttpStatusCode upstreamCode, string body, string operation)
    {
        return (int)upstreamCode switch
        {
            StatusCodes.Status400BadRequest => new ApiErrorException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                $"Accounts Manager operation {operation} вернул ошибку валидации.",
                CreateDetails(upstreamCode, body)),

            StatusCodes.Status404NotFound => new ApiErrorException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound,
                $"Accounts Manager operation {operation} не нашёл сущность.",
                CreateDetails(upstreamCode, body)),

            StatusCodes.Status409Conflict => new ApiErrorException(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.Conflict,
                $"Accounts Manager operation {operation} вернул конфликт.",
                CreateDetails(upstreamCode, body)),

            _ => new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                $"Accounts Manager operation {operation} завершился ошибкой.",
                CreateDetails(upstreamCode, body)),
        };
    }

    private static Dictionary<string, object?> CreateDetails(HttpStatusCode statusCode, string body)
    {
        return new Dictionary<string, object?>
        {
            ["upstreamStatusCode"] = (int)statusCode,
            ["upstreamBody"] = body,
        };
    }

    private static AccountsManagerAccountTypeDefinition ToDefinition(InternalAccountTypeDto dto)
    {
        return new AccountsManagerAccountTypeDefinition(
            dto.AccountTypeId,
            dto.Platform,
            dto.DisplayName,
            dto.Description,
            dto.WorkerProfileId,
            dto.Enabled,
            dto.SortOrder,
            dto.FormFields.Select(field => new AccountsManagerAccountTypeField(
                field.Key,
                field.Label,
                field.InputType,
                field.Required,
                field.Secret,
                field.Placeholder,
                field.DefaultValue)).ToList(),
            new AccountsManagerAccountTypeRuntime(
                dto.Runtime.AutospawnEnabled,
                dto.Runtime.WorkerImage,
                dto.Runtime.WorkerPathPrefix,
                dto.Runtime.HealthPath,
                dto.Runtime.ContainerPort,
                dto.Runtime.EnvironmentVariables,
                dto.Runtime.WorkerCommand));
    }

    private static AccountsManagerWorkerServerDefinition ToDefinition(InternalWorkerServerDto dto)
    {
        return new AccountsManagerWorkerServerDefinition(
            dto.ServerId,
            dto.BaseUrlTemplate,
            dto.Status,
            dto.Health,
            dto.Capacity,
            dto.CurrentLoad,
            dto.DockerHost,
            dto.DockerNetwork,
            dto.LastHeartbeatAtUtc,
            new AccountsManagerWorkerServerRegistrySummary(
                dto.Registry.Enabled,
                dto.Registry.Host,
                dto.Registry.Username,
                dto.Registry.HasToken,
                dto.Registry.TokenUpdatedAtUtc),
            dto.Metadata);
    }

    private sealed record InternalAccountTypeListResponse(IReadOnlyList<InternalAccountTypeDto> Items);

    private sealed record InternalAccountTypeResponse(InternalAccountTypeDto AccountType);

    private sealed record InternalAccountTypeDto(
        string AccountTypeId,
        string Platform,
        string DisplayName,
        string? Description,
        string WorkerProfileId,
        bool Enabled,
        int SortOrder,
        IReadOnlyList<InternalAccountTypeFieldDto> FormFields,
        InternalAccountTypeRuntimeDto Runtime);

    private sealed record InternalAccountTypeFieldDto(
        string Key,
        string Label,
        string InputType,
        bool Required,
        bool Secret,
        string? Placeholder,
        string? DefaultValue);

    private sealed record InternalAccountTypeRuntimeDto(
        bool AutospawnEnabled,
        string WorkerImage,
        string WorkerPathPrefix,
        string HealthPath,
        int ContainerPort,
        IReadOnlyDictionary<string, string> EnvironmentVariables,
        IReadOnlyList<string>? WorkerCommand);

    private sealed record InternalWorkerServerListResponse(IReadOnlyList<InternalWorkerServerDto> Items);

    private sealed record InternalWorkerServerResponse(InternalWorkerServerDto WorkerServer);

    private sealed record InternalWorkerServerDto(
        string ServerId,
        string BaseUrlTemplate,
        string Status,
        string Health,
        int Capacity,
        int CurrentLoad,
        string? DockerHost,
        string? DockerNetwork,
        DateTimeOffset? LastHeartbeatAtUtc,
        InternalWorkerServerRegistryDto Registry,
        IReadOnlyDictionary<string, object?> Metadata);

    private sealed record InternalWorkerServerRegistryDto(
        bool Enabled,
        string Host,
        string? Username,
        bool HasToken,
        DateTimeOffset? TokenUpdatedAtUtc);
}
