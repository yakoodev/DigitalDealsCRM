using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DDCRM.Entitlement.Persistence.DesignTime;

public sealed class EntitlementDbContextFactory : IDesignTimeDbContextFactory<EntitlementDbContext>
{
    public EntitlementDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<EntitlementDbContext>();
        var connectionString =
            Environment.GetEnvironmentVariable("ENTITLEMENT_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=ddcrm_entitlement;Username=postgres;Password=postgres";

        optionsBuilder.UseNpgsql(connectionString);
        return new EntitlementDbContext(optionsBuilder.Options);
    }
}
