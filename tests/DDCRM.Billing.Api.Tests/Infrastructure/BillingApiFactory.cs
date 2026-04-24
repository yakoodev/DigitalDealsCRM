using DDCRM.Billing.Persistence;
using DDCRM.Billing.Persistence.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DDCRM.Billing.Api.Tests.Infrastructure;

public sealed class BillingApiFactory : WebApplicationFactory<Program>
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
                ["TEST_INMEMORY_DB_NAME"] = $"billing-tests-{Guid.NewGuid():N}",
            });
        });
    }

    public BillingPaymentEntity? FindPayment(Guid paymentId)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        return dbContext.Payments.AsNoTracking().SingleOrDefault(x => x.Id == paymentId);
    }

    public BillingSubscriptionEntity? FindSubscription(Guid projectId)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        return dbContext.Subscriptions.AsNoTracking().SingleOrDefault(x => x.ProjectId == projectId);
    }

    public int CountWebhookEvents(string eventId)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        return dbContext.WebhookEvents.AsNoTracking().Count(x => x.EventId == eventId);
    }

    public int CountRefunds(Guid paymentId)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        return dbContext.Refunds.AsNoTracking().Count(x => x.PaymentId == paymentId);
    }

    public void ForceSubscriptionGraceExpired(Guid projectId)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var subscription = dbContext.Subscriptions.Single(x => x.ProjectId == projectId);
        subscription.Status = "grace";
        subscription.GraceEndsAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        subscription.UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        dbContext.SaveChanges();
    }
}
