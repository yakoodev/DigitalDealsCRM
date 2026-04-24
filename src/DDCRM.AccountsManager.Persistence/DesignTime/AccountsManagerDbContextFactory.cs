using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DDCRM.AccountsManager.Persistence.DesignTime;

public sealed class AccountsManagerDbContextFactory : IDesignTimeDbContextFactory<AccountsManagerDbContext>
{
    public AccountsManagerDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AccountsManagerDbContext>();
        var connectionString =
            Environment.GetEnvironmentVariable("ACCOUNTS_MANAGER_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=ddcrm_accounts_manager;Username=postgres;Password=postgres";

        optionsBuilder.UseNpgsql(connectionString);
        return new AccountsManagerDbContext(optionsBuilder.Options);
    }
}
