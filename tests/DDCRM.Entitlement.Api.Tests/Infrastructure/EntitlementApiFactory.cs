using DDCRM.Entitlement.Persistence;
using DDCRM.Entitlement.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DDCRM.Entitlement.Api.Tests.Infrastructure;

public sealed class EntitlementApiFactory : WebApplicationFactory<Program>
{
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
                ["TEST_INMEMORY_DB_NAME"] = $"entitlement-tests-{Guid.NewGuid():N}",
            });
        });
    }

    public EntitlementSnapshotEntity? FindSnapshot(Guid projectId)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EntitlementDbContext>();
        return dbContext.Snapshots.AsNoTracking().SingleOrDefault(x => x.ProjectId == projectId);
    }

    public int CountOverrides(Guid projectId)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EntitlementDbContext>();
        return dbContext.Overrides.AsNoTracking().Count(x => x.ProjectId == projectId);
    }

    public int CountAudits(Guid projectId, string operation)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EntitlementDbContext>();
        return dbContext.Audits.AsNoTracking().Count(x => x.ProjectId == projectId && x.Operation == operation);
    }
}
