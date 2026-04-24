using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDCRM.AccountsManager.Api.Tests.Infrastructure;

namespace DDCRM.AccountsManager.Api.Tests;

public sealed class AccountsManagerApiIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task LifecycleCreateMigrateDelete_ManagesPlacementAndRoutes()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var accountId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        using var createRequest = CreateMutatingRequest(
            "/internal/v1/lifecycle/create",
            Guid.NewGuid().ToString("N"),
            new
            {
                accountId,
                projectId,
                platform = "avito",
                proxyConfig = new
                {
                    host = "127.0.0.1",
                    port = 8080,
                },
            });

        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);

        var afterCreate = factory.FindPlacement(accountId);
        Assert.NotNull(afterCreate);
        Assert.Equal(projectId, afterCreate!.ProjectId);
        Assert.Equal(1, afterCreate.RouteVersion);
        Assert.True(afterCreate.ProxyConfigured);
        Assert.Single(factory.RouteRegistryClient.Upserts);

        using var updateRequest = CreateMutatingRequest(
            "/internal/v1/lifecycle/update",
            Guid.NewGuid().ToString("N"),
            new
            {
                accountId,
                proxyConfig = new
                {
                    host = "127.0.0.2",
                    port = 8081,
                },
            });

        var updateResponse = await client.SendAsync(updateRequest);
        Assert.Equal(HttpStatusCode.Accepted, updateResponse.StatusCode);

        using var migrateRequest = CreateMutatingRequest(
            "/internal/v1/lifecycle/migrate",
            Guid.NewGuid().ToString("N"),
            new
            {
                accountId,
                targetServerId = "srv-2",
            });

        var migrateResponse = await client.SendAsync(migrateRequest);
        Assert.Equal(HttpStatusCode.Accepted, migrateResponse.StatusCode);

        var afterMigrate = factory.FindPlacement(accountId);
        Assert.NotNull(afterMigrate);
        Assert.Equal("srv-2", afterMigrate!.ServerId);
        Assert.Equal(2, afterMigrate.RouteVersion);
        var switchCall = Assert.Single(factory.RouteRegistryClient.Switches);
        Assert.Equal(accountId, switchCall.AccountId);

        using var deleteRequest = CreateMutatingRequest(
            "/internal/v1/lifecycle/delete",
            Guid.NewGuid().ToString("N"),
            new
            {
                accountId,
            });

        var deleteResponse = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.Accepted, deleteResponse.StatusCode);

        Assert.Null(factory.FindPlacement(accountId));
        Assert.Single(factory.RouteRegistryClient.Deletes);
        Assert.Equal(4, factory.CountLifecycleAudits(accountId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LifecycleCreate_WithSameIdempotencyKey_IsIdempotent()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var accountId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString("N");

        using var first = CreateMutatingRequest(
            "/internal/v1/lifecycle/create",
            idempotencyKey,
            new
            {
                accountId,
                projectId,
                platform = "ozon",
                proxyConfig = new
                {
                    host = "127.0.0.1",
                    port = 18080,
                },
            });

        using var second = CreateMutatingRequest(
            "/internal/v1/lifecycle/create",
            idempotencyKey,
            new
            {
                accountId,
                projectId,
                platform = "ozon",
                proxyConfig = new
                {
                    host = "127.0.0.1",
                    port = 18080,
                },
            });

        var firstResponse = await client.SendAsync(first);
        var secondResponse = await client.SendAsync(second);

        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, secondResponse.StatusCode);
        Assert.Single(factory.RouteRegistryClient.Upserts);
        Assert.Equal(1, factory.CountLifecycleAudits(accountId));

        using var firstJson = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            firstJson.RootElement.GetProperty("requestId").GetString(),
            secondJson.RootElement.GetProperty("requestId").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LifecycleUpdate_WithoutProxyConfig_ReturnsBadRequest()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var accountId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        using var createRequest = CreateMutatingRequest(
            "/internal/v1/lifecycle/create",
            Guid.NewGuid().ToString("N"),
            new
            {
                accountId,
                projectId,
                platform = "wb",
                proxyConfig = new
                {
                    host = "127.0.0.1",
                    port = 28080,
                },
            });
        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);

        using var updateRequest = CreateMutatingRequest(
            "/internal/v1/lifecycle/update",
            Guid.NewGuid().ToString("N"),
            new
            {
                accountId,
            });

        var updateResponse = await client.SendAsync(updateRequest);
        Assert.Equal(HttpStatusCode.BadRequest, updateResponse.StatusCode);

        using var payload = JsonDocument.Parse(await updateResponse.Content.ReadAsStringAsync());
        Assert.Equal("VALIDATION_ERROR", payload.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LifecycleDelete_WhenPlacementMissing_StillDeletesRoute()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var accountId = Guid.NewGuid();
        using var request = CreateMutatingRequest(
            "/internal/v1/lifecycle/delete",
            Guid.NewGuid().ToString("N"),
            new
            {
                accountId,
            });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var deleteCall = Assert.Single(factory.RouteRegistryClient.Deletes);
        Assert.Equal(accountId, deleteCall.AccountId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LifecycleCreate_WhenRouteRegistryFails_DoesNotPersistPlacement()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        factory.RouteRegistryClient.FailNextUpsertRequest();

        var accountId = Guid.NewGuid();
        using var request = CreateMutatingRequest(
            "/internal/v1/lifecycle/create",
            Guid.NewGuid().ToString("N"),
            new
            {
                accountId,
                projectId = Guid.NewGuid(),
                platform = "yandex-market",
                proxyConfig = new
                {
                    host = "127.0.0.1",
                    port = 38080,
                },
            });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Null(factory.FindPlacement(accountId));
        Assert.Equal(0, factory.CountLifecycleAudits(accountId));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task LifecycleCreate_WithoutServiceToken_ReturnsUnauthorized()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();

        using var request = CreateMutatingRequest(
            "/internal/v1/lifecycle/create",
            Guid.NewGuid().ToString("N"),
            new
            {
                accountId = Guid.NewGuid(),
                projectId = Guid.NewGuid(),
                platform = "avito",
                proxyConfig = new
                {
                    host = "127.0.0.1",
                    port = 8080,
                },
            });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task LifecycleCreate_WithWorkerToken_ReturnsForbidden()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        using var request = CreateMutatingRequest(
            "/internal/v1/lifecycle/create",
            Guid.NewGuid().ToString("N"),
            new
            {
                accountId = Guid.NewGuid(),
                projectId = Guid.NewGuid(),
                platform = "avito",
                proxyConfig = new
                {
                    host = "127.0.0.1",
                    port = 8080,
                },
            });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static HttpRequestMessage CreateMutatingRequest(string path, string idempotencyKey, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }
}
