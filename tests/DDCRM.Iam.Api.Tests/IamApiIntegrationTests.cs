using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDCRM.Iam.Api.Tests.Infrastructure;

namespace DDCRM.Iam.Api.Tests;

public sealed class IamApiIntegrationTests(IamApiFactory factory) : IClassFixture<IamApiFactory>
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task CheckPermission_ReturnsAllowedForConfiguredRole()
    {
        factory.EnsureSeeded();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var response = await client.PostAsJsonAsync("/internal/v1/iam/check-permission", new
        {
            projectId = IamApiFactory.SeedData.ProjectId,
            userId = IamApiFactory.SeedData.UserId,
            permission = "project.accounts.view",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var allowed = json.RootElement.GetProperty("data").GetProperty("allowed").GetBoolean();
        Assert.True(allowed);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task RequestWithoutServiceToken_ReturnsUnauthorized()
    {
        factory.EnsureSeeded();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/internal/v1/iam/check-permission", new
        {
            projectId = IamApiFactory.SeedData.ProjectId,
            userId = IamApiFactory.SeedData.UserId,
            permission = "project.accounts.view",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task WorkerTokenAgainstInternalApi_ReturnsForbidden()
    {
        factory.EnsureSeeded();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var response = await client.PostAsJsonAsync("/internal/v1/iam/check-permission", new
        {
            projectId = IamApiFactory.SeedData.ProjectId,
            userId = IamApiFactory.SeedData.UserId,
            permission = "project.accounts.view",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MembershipCacheInvalidate_IsIdempotent()
    {
        factory.EnsureSeeded();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var idempotencyKey = Guid.NewGuid().ToString("N");

        using var first = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/iam/membership-cache/invalidate")
        {
            Content = JsonContent.Create(new
            {
                projectId = IamApiFactory.SeedData.ProjectId,
                userId = IamApiFactory.SeedData.UserId,
                actor = "test-suite",
                reason = "integration-test",
            })
        };
        first.Headers.Add("Idempotency-Key", idempotencyKey);

        using var second = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/iam/membership-cache/invalidate")
        {
            Content = JsonContent.Create(new
            {
                projectId = IamApiFactory.SeedData.ProjectId,
                userId = IamApiFactory.SeedData.UserId,
                actor = "test-suite",
                reason = "integration-test",
            })
        };
        second.Headers.Add("Idempotency-Key", idempotencyKey);

        var firstResponse = await client.SendAsync(first);
        var secondResponse = await client.SendAsync(second);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
    }
}
