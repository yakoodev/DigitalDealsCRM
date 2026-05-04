namespace DDCRM.Core.Api.Integrations;

public sealed class ProjectServiceIntegrationRegistry(IEnumerable<IProjectServiceIntegrationClient> clients)
{
    private readonly Dictionary<string, IProjectServiceIntegrationClient> _clients = clients
        .ToDictionary(x => x.IntegrationKey, StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string integrationKey, out IProjectServiceIntegrationClient client)
    {
        return _clients.TryGetValue(integrationKey, out client!);
    }
}
