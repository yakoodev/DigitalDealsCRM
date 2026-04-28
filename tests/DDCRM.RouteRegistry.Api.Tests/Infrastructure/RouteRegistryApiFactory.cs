using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace DDCRM.RouteRegistry.Api.Tests.Infrastructure;

public sealed class RouteRegistryApiFactory : WebApplicationFactory<Program>
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
                ["TEST_INMEMORY_DB_NAME"] = $"route-tests-{Guid.NewGuid():N}",
            });
        });
    }
}
