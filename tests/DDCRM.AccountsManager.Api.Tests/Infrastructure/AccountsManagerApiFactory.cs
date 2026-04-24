using DDCRM.AccountsManager.Api.RouteRegistry;
using DDCRM.AccountsManager.Api.Worker;
using DDCRM.AccountsManager.Persistence;
using DDCRM.AccountsManager.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDCRM.AccountsManager.Api.Tests.Infrastructure;

public sealed class AccountsManagerApiFactory : WebApplicationFactory<Program>
{
    public RecordingRouteRegistryClient RouteRegistryClient { get; } = new();
    public RecordingWorkerControlClient WorkerControlClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["INTERNAL_API_SERVICE_AUTH_ENABLED"] = "true",
                ["INTERNAL_API_SERVICE_AUTH_ACCEPTED_TOKENS"] = "internal-token-a,internal-token-b",
                ["WORKER_API_SERVICE_AUTH_ACCEPTED_TOKENS"] = "worker-token-a,worker-token-b",
                ["TEST_USE_INMEMORY_DB"] = "true",
                ["TEST_INMEMORY_DB_NAME"] = $"accounts-manager-tests-{Guid.NewGuid():N}",
                ["ROUTE_REGISTRY_CLIENT_ENABLED"] = "false",
                ["WORKER_CONTROL_CLIENT_ENABLED"] = "false",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRouteRegistryClient>();
            services.RemoveAll<RecordingRouteRegistryClient>();
            services.RemoveAll<IWorkerControlClient>();
            services.RemoveAll<RecordingWorkerControlClient>();

            services.AddSingleton(RouteRegistryClient);
            services.AddSingleton<IRouteRegistryClient>(serviceProvider => serviceProvider.GetRequiredService<RecordingRouteRegistryClient>());

            services.AddSingleton(WorkerControlClient);
            services.AddSingleton<IWorkerControlClient>(serviceProvider => serviceProvider.GetRequiredService<RecordingWorkerControlClient>());
        });
    }

    public WorkerPlacementEntity? FindPlacement(Guid accountId)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AccountsManagerDbContext>();
        return dbContext.WorkerPlacements.AsNoTracking().SingleOrDefault(x => x.AccountId == accountId);
    }

    public int CountLifecycleAudits(Guid accountId)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AccountsManagerDbContext>();
        return dbContext.LifecycleAudits.AsNoTracking().Count(x => x.AccountId == accountId);
    }
}
