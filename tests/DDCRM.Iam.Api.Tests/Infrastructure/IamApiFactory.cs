using DDCRM.Core.Persistence;
using DDCRM.Core.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DDCRM.Iam.Api.Tests.Infrastructure;

public sealed class IamApiFactory : WebApplicationFactory<Program>
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
                ["TEST_INMEMORY_DB_NAME"] = $"iam-tests-{Guid.NewGuid():N}",
            });
        });
    }

    public void EnsureSeeded()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

        if (dbContext.Projects.Any(x => x.Id == SeedData.ProjectId))
        {
            return;
        }

        dbContext.Projects.Add(new ProjectEntity
        {
            Id = SeedData.ProjectId,
            Name = "IAM Seed Project",
            Status = "active",
            OwnerUserId = SeedData.UserId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        dbContext.ProjectMembers.Add(new ProjectMemberEntity
        {
            ProjectId = SeedData.ProjectId,
            UserId = SeedData.UserId,
            Role = "admin",
            JoinedAtUtc = DateTimeOffset.UtcNow,
        });

        dbContext.SaveChanges();
    }

    public static class SeedData
    {
        public static Guid ProjectId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

        public static Guid UserId { get; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
    }
}
