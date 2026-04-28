using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DDCRM.Billing.Persistence.DesignTime;

public sealed class BillingDbContextFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BillingDbContext>();
        var connectionString =
            Environment.GetEnvironmentVariable("BILLING_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=ddcrm_billing;Username=postgres;Password=postgres";

        optionsBuilder.UseNpgsql(connectionString);
        return new BillingDbContext(optionsBuilder.Options);
    }
}
