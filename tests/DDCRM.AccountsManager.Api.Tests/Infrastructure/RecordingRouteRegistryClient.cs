using DDCRM.AccountsManager.Api.RouteRegistry;
using DDCRM.Shared.Errors;
using Microsoft.AspNetCore.Http;

namespace DDCRM.AccountsManager.Api.Tests.Infrastructure;

public sealed class RecordingRouteRegistryClient : IRouteRegistryClient
{
    private readonly object _lock = new();
    private readonly List<RouteUpsertCall> _upserts = [];
    private readonly List<RouteDeleteCall> _deletes = [];
    private readonly List<RouteSwitchCall> _switches = [];
    private bool _failNextUpsert;

    public IReadOnlyList<RouteUpsertCall> Upserts
    {
        get
        {
            lock (_lock)
            {
                return _upserts.ToArray();
            }
        }
    }

    public IReadOnlyList<RouteDeleteCall> Deletes
    {
        get
        {
            lock (_lock)
            {
                return _deletes.ToArray();
            }
        }
    }

    public IReadOnlyList<RouteSwitchCall> Switches
    {
        get
        {
            lock (_lock)
            {
                return _switches.ToArray();
            }
        }
    }

    public void FailNextUpsertRequest()
    {
        lock (_lock)
        {
            _failNextUpsert = true;
        }
    }

    public Task UpsertAsync(Guid accountId, RouteUpsertRequestDto request, string idempotencyKey, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_failNextUpsert)
            {
                _failNextUpsert = false;
                throw new ApiErrorException(
                    StatusCodes.Status502BadGateway,
                    ApiErrorCodes.InternalError,
                    "Route Registry недоступен или вернул ошибку.");
            }

            _upserts.Add(new RouteUpsertCall(accountId, request, idempotencyKey));
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid accountId, string idempotencyKey, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _deletes.Add(new RouteDeleteCall(accountId, idempotencyKey));
        }

        return Task.CompletedTask;
    }

    public Task SwitchAsync(Guid accountId, RouteSwitchRequestDto request, string idempotencyKey, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _switches.Add(new RouteSwitchCall(accountId, request, idempotencyKey));
        }

        return Task.CompletedTask;
    }
}

public sealed record RouteUpsertCall(Guid AccountId, RouteUpsertRequestDto Request, string IdempotencyKey);

public sealed record RouteDeleteCall(Guid AccountId, string IdempotencyKey);

public sealed record RouteSwitchCall(Guid AccountId, RouteSwitchRequestDto Request, string IdempotencyKey);
