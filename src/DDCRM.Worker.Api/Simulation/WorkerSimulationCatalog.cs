namespace DDCRM.Worker.Api.Simulation;

public static class WorkerSimulationCatalog
{
    private static readonly Dictionary<string, string[]> CapabilitiesByProfile = new(StringComparer.Ordinal)
    {
        [WorkerCapabilityProfiles.CoreV1] =
        [
            "ext.market.sync",
            "ext.market.promote",
            "ext.market.audit",
        ],
        [WorkerCapabilityProfiles.FailuresV1] =
        [
            "ext.market.sync",
            "ext.worker.retry",
            "ext.worker.diagnostics",
        ],
        [WorkerCapabilityProfiles.ContractV1] =
        [
            "ext.market.sync",
            "ext.test.capability-mismatch",
            "ext.test.idempotency-replay",
            "ext.test.timeout",
            "ext.test.transient-error",
            "ext.test.malformed-payload",
            "ext.test.contract-drift",
        ],
    };

    public static bool IsKnownScenario(string scenario) => scenario switch
    {
        WorkerScenarioIds.HappyPath => true,
        WorkerScenarioIds.AuthFail => true,
        WorkerScenarioIds.Timeout => true,
        WorkerScenarioIds.RateLimit => true,
        WorkerScenarioIds.MalformedPayload => true,
        WorkerScenarioIds.CapabilityMismatch => true,
        WorkerScenarioIds.IdempotencyReplay => true,
        WorkerScenarioIds.TransientError => true,
        WorkerScenarioIds.ContractDrift => true,
        _ => false,
    };

    public static bool IsKnownProfile(string profile) => CapabilitiesByProfile.ContainsKey(profile);

    public static IReadOnlyCollection<string> GetCapabilities(string profile, bool allowTestActions)
    {
        if (!CapabilitiesByProfile.TryGetValue(profile, out var capabilities))
        {
            return [];
        }

        return capabilities
            .Where(x => allowTestActions || !x.StartsWith("ext.test.", StringComparison.Ordinal))
            .ToArray();
    }
}
