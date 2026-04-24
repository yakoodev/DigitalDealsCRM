using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DDCRM.Worker.Persistence.DesignTime;

public sealed class WorkerDbContextFactory : IDesignTimeDbContextFactory<WorkerDbContext>
{
    public WorkerDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<WorkerDbContext>();
        var connectionString =
            Environment.GetEnvironmentVariable("WORKER_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=ddcrm_worker;Username=postgres;Password=postgres";

        optionsBuilder.UseNpgsql(connectionString);
        return new WorkerDbContext(optionsBuilder.Options);
    }
}
