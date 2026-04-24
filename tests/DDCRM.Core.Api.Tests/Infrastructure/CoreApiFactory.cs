using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DDCRM.Core.Api.AccountsManager;
using DDCRM.Core.Api.Billing;
using DDCRM.Core.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;

namespace DDCRM.Core.Api.Tests.Infrastructure;

public sealed class CoreApiFactory : WebApplicationFactory<Program>
{
    private const string Issuer = "ddcrm-tests";
    private const string Audience = "ddcrm-tests-api";
    private const string SigningKey = "ddcrm-tests-signing-key-which-is-long-enough-123";

    public FakeBillingClient BillingClient { get; } = new();
    public FakeAccountsManagerClient AccountsManagerClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EXTERNAL_API_JWT_ISSUER"] = Issuer,
                ["EXTERNAL_API_JWT_AUDIENCE"] = Audience,
                ["EXTERNAL_API_JWT_SIGNING_KEY"] = SigningKey,
                ["EXTERNAL_API_CORS_ENABLED"] = "true",
                ["EXTERNAL_API_CORS_ALLOW_CREDENTIALS"] = "true",
                ["EXTERNAL_API_CORS_ALLOW_ORIGINS"] = "https://app.ddcrm.local",
                ["EXTERNAL_API_CORS_ALLOW_METHODS"] = "GET,POST,PATCH,DELETE,OPTIONS",
                ["EXTERNAL_API_CORS_ALLOW_HEADERS"] = "Authorization,Content-Type,Idempotency-Key,X-Request-Id",
                ["EXTERNAL_API_CORS_EXPOSE_HEADERS"] = "X-Request-Id",
                ["EXTERNAL_API_CORS_MAX_AGE_SECONDS"] = "600",
                ["TEST_USE_INMEMORY_DB"] = "true",
                ["TEST_INMEMORY_DB_NAME"] = $"core-tests-{Guid.NewGuid():N}",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBillingClient>();
            services.RemoveAll<FakeBillingClient>();
            services.RemoveAll<IAccountsManagerClient>();
            services.RemoveAll<FakeAccountsManagerClient>();

            services.AddSingleton(BillingClient);
            services.AddSingleton<IBillingClient>(serviceProvider => serviceProvider.GetRequiredService<FakeBillingClient>());

            services.AddSingleton(AccountsManagerClient);
            services.AddSingleton<IAccountsManagerClient>(serviceProvider => serviceProvider.GetRequiredService<FakeAccountsManagerClient>());
        });
    }

    public string CreateToken(Guid userId)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public int CountProxyCredentialsAudits(Guid projectId, Guid accountId)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        return dbContext.ProxyCredentialsAudits.AsNoTracking().Count(x => x.ProjectId == projectId && x.AccountId == accountId);
    }
}
