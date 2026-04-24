using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DDCRM.RouteRegistry.Persistence.DesignTime;

public sealed class RouteRegistryDbContextFactory : IDesignTimeDbContextFactory<RouteRegistryDbContext>
{
    public RouteRegistryDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<RouteRegistryDbContext>();
        var connectionString =
            Environment.GetEnvironmentVariable("ROUTE_REGISTRY_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=ddcrm_route_registry;Username=postgres;Password=postgres";

        optionsBuilder.UseNpgsql(connectionString);
        return new RouteRegistryDbContext(optionsBuilder.Options);
    }
}
