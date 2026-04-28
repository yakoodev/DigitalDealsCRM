namespace DDCRM.Gateway.Api.Clients;

public sealed record WorkerBinding(string ServerId, string WorkerId, string? PodId);

public sealed record RouteResolution(
    string RouteKey,
    Guid AccountId,
    Guid ProjectId,
    int RouteVersion,
    WorkerBinding WorkerBinding);
