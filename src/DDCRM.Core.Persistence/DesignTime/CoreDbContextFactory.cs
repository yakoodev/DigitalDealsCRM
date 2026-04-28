using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DDCRM.Core.Persistence.DesignTime;

public sealed class CoreDbContextFactory : IDesignTimeDbContextFactory<CoreDbContext>
{
    public CoreDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CoreDbContext>();
        var connectionString =
            Environment.GetEnvironmentVariable("CORE_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=ddcrm_core;Username=postgres;Password=postgres";

        optionsBuilder.UseNpgsql(connectionString);
        return new CoreDbContext(optionsBuilder.Options);
    }
}
