namespace DDCRM.AccountsManager.Api.RouteRegistry;

public sealed record WorkerBindingDto(string ServerId, string WorkerId, string? PodId);

public sealed record RouteUpsertRequestDto(Guid ProjectId, string RouteKey, WorkerBindingDto WorkerBinding, int RouteVersion);

public sealed record RouteSwitchRequestDto(WorkerBindingDto WorkerBinding, int RouteVersion);

public interface IRouteRegistryClient
{
    Task UpsertAsync(Guid accountId, RouteUpsertRequestDto request, string idempotencyKey, CancellationToken cancellationToken);

    Task DeleteAsync(Guid accountId, string idempotencyKey, CancellationToken cancellationToken);

    Task SwitchAsync(Guid accountId, RouteSwitchRequestDto request, string idempotencyKey, CancellationToken cancellationToken);
}
