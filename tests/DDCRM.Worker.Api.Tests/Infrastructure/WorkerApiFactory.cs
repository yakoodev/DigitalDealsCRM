using DDCRM.Worker.Api.Simulation;
using DDCRM.Worker.Persistence;
using DDCRM.Worker.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DDCRM.Worker.Api.Tests.Infrastructure;

public sealed class WorkerApiFactory : WebApplicationFactory<Program>
{
    private readonly IReadOnlyDictionary<string, string?> _overrides;

    public WorkerApiFactory(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        _overrides = overrides ?? new Dictionary<string, string?>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["WORKER_API_SERVICE_AUTH_ENABLED"] = "true",
                ["WORKER_API_SERVICE_AUTH_ACCEPTED_TOKENS"] = "worker-token-a,worker-token-b",
                ["INTERNAL_API_SERVICE_AUTH_ACCEPTED_TOKENS"] = "internal-token-a,internal-token-b",
                ["TEST_USE_INMEMORY_DB"] = "true",
                ["TEST_INMEMORY_DB_NAME"] = $"worker-tests-{Guid.NewGuid():N}",
                ["TEST_WORKER_ENABLED"] = "true",
                ["TEST_WORKER_SCENARIO"] = WorkerScenarioIds.HappyPath,
                ["TEST_WORKER_CAPABILITY_PROFILE"] = WorkerCapabilityProfiles.CoreV1,
                ["TEST_WORKER_ALLOWED_ENVIRONMENTS"] = "development,local,ci,staging",
                ["TEST_WORKER_BLOCK_IN_PRODUCTION"] = "true",
                ["TEST_WORKER_EXT_ACTIONS_ENABLED"] = "true",
                ["TEST_WORKER_PROVIDER"] = "platimarket",
                ["TEST_WORKER_FIXTURE_SOURCE"] = "./fixtures/test-worker",
                ["TEST_WORKER_FIXTURE_REVISION"] = "main",
            };

            foreach (var (key, value) in _overrides)
            {
                settings[key] = value;
            }

            configurationBuilder.AddInMemoryCollection(settings);
        });
    }

    public WorkerListingEntity? FindListing(string listingId)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        return dbContext.Listings.AsNoTracking().SingleOrDefault(x => x.Id == listingId);
    }

    public WorkerProxyCredentialsEntity? FindProxyCredentials(Guid accountId)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
        return dbContext.ProxyCredentials.AsNoTracking().SingleOrDefault(x => x.AccountId == accountId);
    }
}
