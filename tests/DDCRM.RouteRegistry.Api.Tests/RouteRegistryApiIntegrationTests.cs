using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDCRM.RouteRegistry.Api.Tests.Infrastructure;

namespace DDCRM.RouteRegistry.Api.Tests;

public sealed class RouteRegistryApiIntegrationTests(RouteRegistryApiFactory factory) : IClassFixture<RouteRegistryApiFactory>
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpsertThenResolveBulk_ReturnsRoute()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var accountId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        using var upsert = new HttpRequestMessage(HttpMethod.Put, $"/internal/v1/routes/{accountId}")
        {
            Content = JsonContent.Create(new
            {
                projectId,
                routeKey = "route.alpha",
                workerBinding = new
                {
                    serverId = "srv-1",
                    workerId = "worker-1",
                    podId = "pod-1",
                },
                routeVersion = 1,
            })
        };
        upsert.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var upsertResponse = await client.SendAsync(upsert);
        Assert.Equal(HttpStatusCode.OK, upsertResponse.StatusCode);

        var resolveResponse = await client.PostAsJsonAsync("/internal/v1/routes/resolve-bulk", new
        {
            routeKeys = new[] { "route.alpha" },
        });

        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        using var json = JsonDocument.Parse(await resolveResponse.Content.ReadAsStringAsync());
        var item = json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().Single();

        Assert.Equal(accountId, item.GetProperty("accountId").GetGuid());
        Assert.Equal(projectId, item.GetProperty("projectId").GetGuid());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SwitchWithOutdatedVersion_ReturnsConflict()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var accountId = Guid.NewGuid();

        using var upsert = new HttpRequestMessage(HttpMethod.Put, $"/internal/v1/routes/{accountId}")
        {
            Content = JsonContent.Create(new
            {
                projectId = Guid.NewGuid(),
                routeKey = "route.beta",
                workerBinding = new
                {
                    serverId = "srv-1",
                    workerId = "worker-1",
                    podId = "pod-1",
                },
                routeVersion = 2,
            })
        };
        upsert.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        await client.SendAsync(upsert);

        using var switchRequest = new HttpRequestMessage(HttpMethod.Post, $"/internal/v1/routes/{accountId}/switch")
        {
            Content = JsonContent.Create(new
            {
                workerBinding = new
                {
                    serverId = "srv-2",
                    workerId = "worker-2",
                    podId = "pod-2",
                },
                routeVersion = 1,
            })
        };
        switchRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var response = await client.SendAsync(switchRequest);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task MissingServiceToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/internal/v1/routes/resolve-bulk", new
        {
            routeKeys = new[] { "route.any" },
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
