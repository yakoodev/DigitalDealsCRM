namespace DDCRM.Core.Api.Integrations;

public sealed class IntegrationWorkerRuntimeOptions
{
    public const string SectionName = "IntegrationWorkerRuntime";

    public string SteamPlatform { get; set; } = "steam-integration";

    public string DefaultProxyHost { get; set; } = "127.0.0.1";

    public int DefaultProxyPort { get; set; } = 8080;

    public string DefaultProxyLogin { get; set; } = "integration-runtime";

    public string DefaultProxyPassword { get; set; } = "integration-runtime";
}
