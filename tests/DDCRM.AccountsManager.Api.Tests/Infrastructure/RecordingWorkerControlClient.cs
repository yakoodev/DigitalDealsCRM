using DDCRM.AccountsManager.Api.RouteRegistry;
using DDCRM.AccountsManager.Api.Worker;
using DDCRM.Shared.Errors;
using Microsoft.AspNetCore.Http;

namespace DDCRM.AccountsManager.Api.Tests.Infrastructure;

public sealed class RecordingWorkerControlClient : IWorkerControlClient
{
    private bool _failNextApply;

    public List<WorkerControlApplyCall> ApplyCalls { get; } = [];

    public Task ApplyProxyCredentialsAsync(
        WorkerBindingDto workerBinding,
        Guid accountId,
        Dictionary<string, object?> proxyConfig,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (_failNextApply)
        {
            _failNextApply = false;
            throw new ApiErrorException(
                StatusCodes.Status502BadGateway,
                ApiErrorCodes.InternalError,
                "Simulated worker control failure.");
        }

        ApplyCalls.Add(new WorkerControlApplyCall(
            workerBinding,
            accountId,
            new Dictionary<string, object?>(proxyConfig, StringComparer.Ordinal),
            idempotencyKey));

        return Task.CompletedTask;
    }

    public void FailNextApplyRequest() => _failNextApply = true;

    public void Reset()
    {
        _failNextApply = false;
        ApplyCalls.Clear();
    }
}

public sealed record WorkerControlApplyCall(
    WorkerBindingDto WorkerBinding,
    Guid AccountId,
    IReadOnlyDictionary<string, object?> ProxyConfig,
    string IdempotencyKey);
