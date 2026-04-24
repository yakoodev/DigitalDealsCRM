namespace DDCRM.Worker.Api.Simulation;

public sealed class TestWorkerOptions
{
    public bool Enabled { get; set; }

    public string DefaultVisibility { get; set; } = "hidden";

    public string[] AllowedEnvironments { get; set; } = ["local", "ci", "staging", "development"];

    public bool BlockInProduction { get; set; } = true;

    public string Scenario { get; set; } = WorkerScenarioIds.HappyPath;

    public string CapabilityProfile { get; set; } = WorkerCapabilityProfiles.CoreV1;

    public string FixtureSource { get; set; } = "./fixtures/test-worker";

    public string FixtureRevision { get; set; } = "main";

    public bool ExtActionsEnabled { get; set; } = true;
}
