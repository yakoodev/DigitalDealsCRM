using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDCRM.AccountsManager.Api.Tests.Infrastructure;

namespace DDCRM.AccountsManager.Api.Tests;

public sealed class AccountsManagerApiIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerServers_RegisterHeartbeatAndList_Works()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var serverId = "srv-eu-1";
        await RegisterOrUpdateServerAsync(
            client,
            serverId,
            idempotencyKey: Guid.NewGuid().ToString("N"),
            new
            {
                baseUrlTemplate = "http://worker-{serverId}.local",
                status = "active",
                health = "healthy",
                capacity = 12,
                currentLoad = 2,
                metadata = new
                {
                    region = "eu",
                    zone = "a",
                },
            });

        var listResponse = await client.GetAsync("/internal/v1/worker-servers");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        using (var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync()))
        {
            var items = listJson.RootElement.GetProperty("items");
            Assert.Single(items.EnumerateArray());
            var listed = items[0];
            Assert.Equal(serverId, listed.GetProperty("serverId").GetString());
            Assert.Equal(12, listed.GetProperty("capacity").GetInt32());
            Assert.Equal("active", listed.GetProperty("status").GetString());
            Assert.Equal("healthy", listed.GetProperty("health").GetString());
        }

        var heartbeatResponse = await client.PostAsJsonAsync(
            $"/internal/v1/worker-servers/{serverId}/heartbeat",
            new
            {
                health = "degraded",
                currentLoad = 3,
            });
        Assert.Equal(HttpStatusCode.OK, heartbeatResponse.StatusCode);

        var fromDb = factory.FindWorkerServer(serverId);
        Assert.NotNull(fromDb);
        Assert.Equal("degraded", fromDb!.Health);
        Assert.Equal(3, fromDb.CurrentLoad);
        Assert.NotNull(fromDb.LastHeartbeatAtUtc);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LifecycleCreate_WithWorkerRegistry_SelectsLeastLoadedServerAndTracksLoad()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        await RegisterOrUpdateServerAsync(
            client,
            "srv-a",
            Guid.NewGuid().ToString("N"),
            new
            {
                baseUrlTemplate = "http://worker-a.local",
                status = "active",
                health = "healthy",
                capacity = 10,
                currentLoad = 5,
            });
        await RegisterOrUpdateServerAsync(
            client,
            "srv-b",
            Guid.NewGuid().ToString("N"),
            new
            {
                baseUrlTemplate = "http://worker-b.local",
                status = "active",
                health = "healthy",
                capacity = 10,
                currentLoad = 2,
            });

        var accountId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        using var createRequest = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v1/lifecycle/create",
            Guid.NewGuid().ToString("N"),
            new
            {
                accountId,
                projectId,
                platform = "funpay",
                proxyConfig = new
                {
                    host = "127.0.0.1",
                    port = 8080,
                },
            });

        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);

        var placement = factory.FindPlacement(accountId);
        Assert.NotNull(placement);
        Assert.Equal("srv-b", placement!.ServerId);
        Assert.Equal(1, placement.RouteVersion);

        var applyCall = Assert.Single(factory.WorkerControlClient.ApplyCalls);
        Assert.Equal("srv-b", applyCall.WorkerBinding.ServerId);
        Assert.Equal("http://worker-b.local", applyCall.BaseUrlTemplateOverride);
        Assert.Equal(accountId, applyCall.AccountId);

        var upsertCall = Assert.Single(factory.RouteRegistryClient.Upserts);
        Assert.Equal("srv-b", upsertCall.Request.WorkerBinding.ServerId);

        var serverB = factory.FindWorkerServer("srv-b");
        Assert.NotNull(serverB);
        Assert.Equal(3, serverB!.CurrentLoad);

        using var deleteRequest = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v1/lifecycle/delete",
            Guid.NewGuid().ToString("N"),
            new
            {
                accountId,
            });

        var deleteResponse = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.Accepted, deleteResponse.StatusCode);

        serverB = factory.FindWorkerServer("srv-b");
        Assert.NotNull(serverB);
        Assert.Equal(2, serverB!.CurrentLoad);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LifecycleCreate_WithEmptyRegistry_FallsBackToDefaultServer()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var accountId = Guid.NewGuid();
        using var createRequest = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v1/lifecycle/create",
            Guid.NewGuid().ToString("N"),
            new
            {
                accountId,
                projectId = Guid.NewGuid(),
                platform = "playerok",
                proxyConfig = new
                {
                    host = "127.0.0.1",
                    port = 1508,
                },
            });

        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);

        var placement = factory.FindPlacement(accountId);
        Assert.NotNull(placement);
        Assert.Equal("srv-default", placement!.ServerId);

        var applyCall = Assert.Single(factory.WorkerControlClient.ApplyCalls);
        Assert.Equal("srv-default", applyCall.WorkerBinding.ServerId);
        Assert.Null(applyCall.BaseUrlTemplateOverride);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LifecycleMigrate_WithoutTarget_ChoosesBestServerDifferentFromCurrent()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        await RegisterOrUpdateServerAsync(
            client,
            "srv-a",
            Guid.NewGuid().ToString("N"),
            new
            {
                baseUrlTemplate = "http://worker-a.local",
                status = "active",
                health = "healthy",
                capacity = 10,
                currentLoad = 1,
            });
        await RegisterOrUpdateServerAsync(
            client,
            "srv-b",
            Guid.NewGuid().ToString("N"),
            new
            {
                baseUrlTemplate = "http://worker-b.local",
                status = "active",
                health = "healthy",
                capacity = 10,
                currentLoad = 5,
            });

        var accountId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        using var createRequest = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v1/lifecycle/create",
            Guid.NewGuid().ToString("N"),
            new
            {
                accountId,
                projectId,
                platform = "ggsell",
                proxyConfig = new
                {
                    host = "127.0.0.1",
                    port = 8080,
                },
            });
        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);
        Assert.Equal("srv-a", factory.FindPlacement(accountId)!.ServerId);

        var hbA = await client.PostAsJsonAsync(
            "/internal/v1/worker-servers/srv-a/heartbeat",
            new
            {
                currentLoad = 8,
                health = "healthy",
            });
        Assert.Equal(HttpStatusCode.OK, hbA.StatusCode);

        var hbB = await client.PostAsJsonAsync(
            "/internal/v1/worker-servers/srv-b/heartbeat",
            new
            {
                currentLoad = 2,
                health = "healthy",
            });
        Assert.Equal(HttpStatusCode.OK, hbB.StatusCode);

        using var migrateRequest = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v1/lifecycle/migrate",
            Guid.NewGuid().ToString("N"),
            new
            {
                accountId,
            });

        var migrateResponse = await client.SendAsync(migrateRequest);
        Assert.Equal(HttpStatusCode.Accepted, migrateResponse.StatusCode);

        var placement = factory.FindPlacement(accountId);
        Assert.NotNull(placement);
        Assert.Equal("srv-b", placement!.ServerId);
        Assert.Equal(2, placement.RouteVersion);

        var switchCall = Assert.Single(factory.RouteRegistryClient.Switches);
        Assert.Equal("srv-b", switchCall.Request.WorkerBinding.ServerId);

        Assert.Equal(7, factory.FindWorkerServer("srv-a")!.CurrentLoad);
        Assert.Equal(3, factory.FindWorkerServer("srv-b")!.CurrentLoad);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LifecycleRebalance_MigratesPlacementsAndUpdatesLoadCounters()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        await RegisterOrUpdateServerAsync(
            client,
            "srv-a",
            Guid.NewGuid().ToString("N"),
            new
            {
                baseUrlTemplate = "http://worker-a.local",
                status = "active",
                health = "healthy",
                capacity = 10,
                currentLoad = 0,
            });
        await RegisterOrUpdateServerAsync(
            client,
            "srv-b",
            Guid.NewGuid().ToString("N"),
            new
            {
                baseUrlTemplate = "http://worker-b.local",
                status = "active",
                health = "healthy",
                capacity = 10,
                currentLoad = 9,
            });

        var firstAccountId = Guid.NewGuid();
        var secondAccountId = Guid.NewGuid();
        await CreatePlacementAsync(client, firstAccountId, Guid.NewGuid(), "funpay");
        await CreatePlacementAsync(client, secondAccountId, Guid.NewGuid(), "funpay");

        Assert.Equal("srv-a", factory.FindPlacement(firstAccountId)!.ServerId);
        Assert.Equal("srv-a", factory.FindPlacement(secondAccountId)!.ServerId);
        Assert.Equal(2, factory.FindWorkerServer("srv-a")!.CurrentLoad);

        using var rebalanceRequest = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v1/lifecycle/rebalance",
            Guid.NewGuid().ToString("N"),
            new
            {
                maxMoves = 1,
            });
        var rebalanceResponse = await client.SendAsync(rebalanceRequest);
        Assert.Equal(HttpStatusCode.OK, rebalanceResponse.StatusCode);

        using (var json = JsonDocument.Parse(await rebalanceResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, json.RootElement.GetProperty("moved").GetInt32());
            Assert.Equal(1, json.RootElement.GetProperty("migrations").GetArrayLength());
        }

        Assert.Single(factory.RouteRegistryClient.Switches);
        Assert.Equal(1, factory.ListWorkerServers().Count(x => x.ServerId == "srv-a" && x.CurrentLoad == 1));
        Assert.Equal(1, factory.ListWorkerServers().Count(x => x.ServerId == "srv-b" && x.CurrentLoad == 10));
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
            HttpMethod.Post,
            "/internal/v1/lifecycle/create",
            idempotencyKey,
            new
            {
                accountId,
                projectId,
                platform = "funpay",
                proxyConfig = new
                {
                    host = "127.0.0.1",
                    port = 18080,
                },
            });

        using var second = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v1/lifecycle/create",
            idempotencyKey,
            new
            {
                accountId,
                projectId,
                platform = "funpay",
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
        Assert.Single(factory.WorkerControlClient.ApplyCalls);
        Assert.Equal(1, factory.CountLifecycleAudits(accountId));
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
            HttpMethod.Post,
            "/internal/v1/lifecycle/create",
            Guid.NewGuid().ToString("N"),
            new
            {
                accountId,
                projectId = Guid.NewGuid(),
                platform = "platimarket",
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
        Assert.Empty(factory.WorkerControlClient.ApplyCalls);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LifecycleCreate_WhenWorkerControlFails_DoesNotPersistPlacement()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        factory.WorkerControlClient.FailNextApplyRequest();

        var accountId = Guid.NewGuid();
        using var request = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v1/lifecycle/create",
            Guid.NewGuid().ToString("N"),
            new
            {
                accountId,
                projectId = Guid.NewGuid(),
                platform = "platimarket",
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
        Assert.Single(factory.RouteRegistryClient.Upserts);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task WorkerServersList_WithoutServiceToken_ReturnsUnauthorized()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/internal/v1/worker-servers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task WorkerServersList_WithWorkerToken_ReturnsForbidden()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var response = await client.GetAsync("/internal/v1/worker-servers");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task RegisterOrUpdateServerAsync(
        HttpClient client,
        string serverId,
        string idempotencyKey,
        object payload)
    {
        using var request = CreateMutatingRequest(
            HttpMethod.Put,
            $"/internal/v1/worker-servers/{serverId}",
            idempotencyKey,
            payload);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task CreatePlacementAsync(
        HttpClient client,
        Guid accountId,
        Guid projectId,
        string platform)
    {
        using var createRequest = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v1/lifecycle/create",
            Guid.NewGuid().ToString("N"),
            new
            {
                accountId,
                projectId,
                platform,
                proxyConfig = new
                {
                    host = "127.0.0.1",
                    port = 1508,
                },
            });

        var response = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private static HttpRequestMessage CreateMutatingRequest(
        HttpMethod method,
        string path,
        string idempotencyKey,
        object payload)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }
}
