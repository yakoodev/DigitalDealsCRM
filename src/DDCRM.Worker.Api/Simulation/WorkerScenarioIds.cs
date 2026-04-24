namespace DDCRM.Worker.Api.Simulation;

public static class WorkerScenarioIds
{
    public const string HappyPath = "TW-SCN-HAPPY-PATH";
    public const string AuthFail = "TW-SCN-AUTH-FAIL";
    public const string Timeout = "TW-SCN-TIMEOUT";
    public const string RateLimit = "TW-SCN-RATE-LIMIT";
    public const string MalformedPayload = "TW-SCN-MALFORMED-PAYLOAD";
    public const string CapabilityMismatch = "TW-SCN-CAPABILITY-MISMATCH";
    public const string IdempotencyReplay = "TW-SCN-IDEMPOTENCY-REPLAY";
    public const string TransientError = "TW-SCN-TRANSIENT-ERROR";
    public const string ContractDrift = "TW-SCN-CONTRACT-DRIFT";
}
