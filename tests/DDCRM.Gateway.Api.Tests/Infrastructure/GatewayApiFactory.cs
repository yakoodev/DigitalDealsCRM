using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DDCRM.Gateway.Api.Clients;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace DDCRM.Gateway.Api.Tests.Infrastructure;

public sealed class GatewayApiFactory : WebApplicationFactory<Program>
{
    private const string Issuer = "ddcrm-tests";
    private const string Audience = "ddcrm-tests-api";
    private const string SigningKey = "ddcrm-tests-signing-key-which-is-long-enough-123";

    public FakeRouteRegistryClient RouteRegistryClient { get; } = new();

    public FakeIamClient IamClient { get; } = new();

    public FakeEntitlementClient EntitlementClient { get; } = new();

    public FakeWorkerProxyClient WorkerProxyClient { get; } = new();

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
                ["TEST_WORKER_EXT_ACTIONS_ENABLED"] = "true",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRouteRegistryClient>();
            services.RemoveAll<IIamClient>();
            services.RemoveAll<IEntitlementClient>();
            services.RemoveAll<IWorkerProxyClient>();

            services.AddSingleton(RouteRegistryClient);
            services.AddSingleton<IRouteRegistryClient>(serviceProvider => serviceProvider.GetRequiredService<FakeRouteRegistryClient>());

            services.AddSingleton(IamClient);
            services.AddSingleton<IIamClient>(serviceProvider => serviceProvider.GetRequiredService<FakeIamClient>());

            services.AddSingleton(EntitlementClient);
            services.AddSingleton<IEntitlementClient>(serviceProvider => serviceProvider.GetRequiredService<FakeEntitlementClient>());

            services.AddSingleton(WorkerProxyClient);
            services.AddSingleton<IWorkerProxyClient>(serviceProvider => serviceProvider.GetRequiredService<FakeWorkerProxyClient>());
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
}
