namespace DDCRM.Worker.Api;

public static class WorkerErrorCodes
{
    public const string Unavailable = "WORKER_UNAVAILABLE";
    public const string InvalidAction = "WORKER_INVALID_ACTION";
    public const string AuthFailed = "WORKER_AUTH_FAILED";
    public const string RuntimeConflict = "WORKER_RUNTIME_CONFLICT";
    public const string PlatformError = "WORKER_PLATFORM_ERROR";
    public const string InternalError = "INTERNAL_ERROR";
}
