using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDCRM.AccountsManager.Api.Tests.Infrastructure;

namespace DDCRM.AccountsManager.Api.Tests;

public sealed class AccountsManagerApiIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task AccountTypesList_OnCleanStart_ReturnsEmpty()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var response = await client.GetAsync("/internal/v1/account-types");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .ToArray();

        Assert.Empty(items);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AccountTypeUpsert_UpdatesRuntimeTemplate_AndEnforcesSingleActiveProfilePerPlatform()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        using var upsertRequest = CreateMutatingRequest(
            HttpMethod.Put,
            "/internal/v1/account-types/test-worker.funpay",
            Guid.NewGuid().ToString("N"),
            new
            {
                platform = "funpay",
                displayName = "FunPay profile v2",
                description = "Updated runtime profile",
                workerProfileId = "test-worker",
                enabled = true,
                sortOrder = 10,
                formFields = new[]
                {
                    new
                    {
                        key = "displayName",
                        label = "Название аккаунта",
                        inputType = "text",
                        required = true,
                        secret = false,
                        placeholder = "FunPay account",
                        defaultValue = "FunPay account",
                    },
                },
                runtime = new
                {
                    autospawnEnabled = true,
                    workerImage = "ddcrm/worker-api:local",
                    workerPathPrefix = "/internal/v2/worker",
                    healthPath = "/health",
                    containerPort = 8080,
                    environmentVariables = new
                    {
                        TEST_WORKER_PROVIDER = "funpay",
                    },
                },
            });

        var upsertResponse = await client.SendAsync(upsertRequest);
        Assert.Equal(HttpStatusCode.OK, upsertResponse.StatusCode);

        using var conflictRequest = CreateMutatingRequest(
            HttpMethod.Put,
            "/internal/v1/account-types/funpay.custom",
            Guid.NewGuid().ToString("N"),
            new
            {
                platform = "funpay",
                displayName = "FunPay custom",
                workerProfileId = "test-worker",
                enabled = true,
                sortOrder = 11,
                formFields = new[]
                {
                    new
                    {
                        key = "displayName",
                        label = "Название аккаунта",
                        inputType = "text",
                        required = true,
                        secret = false,
                    },
                },
                runtime = new
                {
                    autospawnEnabled = true,
                    workerImage = "ddcrm/worker-api:local",
                    workerPathPrefix = "/internal/v2/worker",
                    healthPath = "/health",
                    containerPort = 8080,
                    environmentVariables = new { },
                },
            });

        var conflictResponse = await client.SendAsync(conflictRequest);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
    }

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
                dockerHost = "unix:///var/run/docker.sock",
                dockerNetwork = "ddcrm_ddcrm",
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
            Assert.Equal("unix:///var/run/docker.sock", listed.GetProperty("dockerHost").GetString());
            Assert.Equal("ddcrm_ddcrm", listed.GetProperty("dockerNetwork").GetString());
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
    public async Task WorkerServers_RegistryToken_IsWriteOnly_AndSupportsClear()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var serverId = "srv-registry";
        using var upsertRequest = CreateMutatingRequest(
            HttpMethod.Put,
            $"/internal/v1/worker-servers/{serverId}",
            Guid.NewGuid().ToString("N"),
            new
            {
                baseUrlTemplate = "http://worker-registry.local",
                status = "active",
                health = "healthy",
                capacity = 10,
                currentLoad = 1,
                registry = new
                {
                    enabled = true,
                    host = "ghcr.io",
                    username = "demo-user",
                    token = "ghp_secret_token",
                },
            });

        var upsertResponse = await client.SendAsync(upsertRequest);
        Assert.Equal(HttpStatusCode.OK, upsertResponse.StatusCode);

        var upsertRaw = await upsertResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ghp_secret_token", upsertRaw, StringComparison.Ordinal);

        using (var upsertJson = JsonDocument.Parse(upsertRaw))
        {
            var registry = upsertJson.RootElement
                .GetProperty("workerServer")
                .GetProperty("registry");
            Assert.True(registry.GetProperty("enabled").GetBoolean());
            Assert.Equal("ghcr.io", registry.GetProperty("host").GetString());
            Assert.Equal("demo-user", registry.GetProperty("username").GetString());
            Assert.True(registry.GetProperty("hasToken").GetBoolean());
            Assert.False(registry.TryGetProperty("token", out _));
        }

        var fromDb = factory.FindWorkerServer(serverId);
        Assert.NotNull(fromDb);
        Assert.NotNull(fromDb!.RegistryTokenEncrypted);
        Assert.NotEqual("ghp_secret_token", fromDb.RegistryTokenEncrypted);

        var listResponse = await client.GetAsync("/internal/v1/worker-servers");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        using (var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync()))
        {
            var listed = listJson.RootElement.GetProperty("items")[0];
            var registry = listed.GetProperty("registry");
            Assert.True(registry.GetProperty("hasToken").GetBoolean());
            Assert.False(registry.TryGetProperty("token", out _));
        }

        using var clearRequest = CreateMutatingRequest(
            HttpMethod.Put,
            $"/internal/v1/worker-servers/{serverId}",
            Guid.NewGuid().ToString("N"),
            new
            {
                registry = new
                {
                    clearToken = true,
                },
            });

        var clearResponse = await client.SendAsync(clearRequest);
        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);

        var cleared = factory.FindWorkerServer(serverId);
        Assert.NotNull(cleared);
        Assert.Null(cleared!.RegistryTokenEncrypted);
        Assert.NotNull(cleared.RegistryTokenUpdatedAtUtc);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LifecycleCreate_WithWorkerRegistry_SelectsLeastLoadedServerAndTracksLoad()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");
        await EnsureActiveAccountTypeAsync(client, "funpay");

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
        await EnsureActiveAccountTypeAsync(client, "playerok");

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
        Assert.Equal("http://worker-api:8080", applyCall.BaseUrlTemplateOverride);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LifecycleCreate_WithMarketplaceAuth_AppliesMarketplaceAuthViaWorkerControl()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");
        await EnsureActiveAccountTypeAsync(client, "funpay");

        var accountId = Guid.NewGuid();
        using var createRequest = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v1/lifecycle/create",
            Guid.NewGuid().ToString("N"),
            new
            {
                accountId,
                projectId = Guid.NewGuid(),
                platform = "funpay",
                proxyConfig = new
                {
                    host = "127.0.0.1",
                    port = 1508,
                },
                marketplaceAuth = new
                {
                    scheme = "golden_key",
                    credentials = new
                    {
                        golden_key = "funpay-golden-key",
                        user_agent = "Mozilla/5.0",
                    },
                },
            });

        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);

        var authApplyCall = Assert.Single(factory.WorkerControlClient.MarketplaceAuthApplyCalls);
        Assert.Equal(accountId, authApplyCall.AccountId);
        Assert.Equal("golden_key", authApplyCall.MarketplaceAuth.Scheme);
        Assert.Equal("funpay-golden-key", authApplyCall.MarketplaceAuth.Credentials["golden_key"]);
        Assert.Equal("Mozilla/5.0", authApplyCall.MarketplaceAuth.Credentials["user_agent"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LifecycleCreate_WithMailConfig_AppliesMailConfigViaWorkerControl()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");
        await EnsureActiveAccountTypeAsync(client, "steam");

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
                platform = "steam",
                proxyConfig = new
                {
                    host = "127.0.0.1",
                    port = 1508,
                },
                mailConfig = new
                {
                    enabled = true,
                    imapHost = "imap.mail.local",
                    imapPort = 993,
                    imapSecurity = "ssl",
                    imapUsername = "steam@mail.local",
                    imapPassword = "mail-secret",
                    mailbox = "INBOX",
                    searchFrom = "noreply@steampowered.com",
                    searchSubject = "Steam",
                },
            });

        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);

        var mailApplyCall = Assert.Single(factory.WorkerControlClient.MailConfigApplyCalls);
        Assert.Equal(projectId, mailApplyCall.ProjectId);
        Assert.Equal(accountId, mailApplyCall.AccountId);
        Assert.True(mailApplyCall.MailConfig.Enabled);
        Assert.Equal("imap.mail.local", mailApplyCall.MailConfig.ImapHost);
        Assert.Equal(993, mailApplyCall.MailConfig.ImapPort);
        Assert.Equal("ssl", mailApplyCall.MailConfig.ImapSecurity);
        Assert.Equal("steam@mail.local", mailApplyCall.MailConfig.ImapUsername);
        Assert.Equal("mail-secret", mailApplyCall.MailConfig.ImapPassword);
        Assert.Equal("INBOX", mailApplyCall.MailConfig.Mailbox);
        Assert.Equal("noreply@steampowered.com", mailApplyCall.MailConfig.SearchFrom);
        Assert.Equal("Steam", mailApplyCall.MailConfig.SearchSubject);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LifecycleCreate_WithInvalidMailConfigPort_ReturnsBadRequest()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");
        await EnsureActiveAccountTypeAsync(client, "steam");

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
                platform = "steam",
                proxyConfig = new
                {
                    host = "127.0.0.1",
                    port = 1508,
                },
                mailConfig = new
                {
                    enabled = true,
                    imapHost = "imap.mail.local",
                    imapPort = 70000,
                    imapSecurity = "ssl",
                    imapUsername = "steam@mail.local",
                    imapPassword = "mail-secret",
                },
            });

        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.BadRequest, createResponse.StatusCode);
        Assert.Empty(factory.WorkerControlClient.MailConfigApplyCalls);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LifecycleCreate_WithMarketplaceAuthTokensWithoutDdg5_ReturnsBadRequest()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");
        await EnsureActiveAccountTypeAsync(client, "playerok");

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
                marketplaceAuth = new
                {
                    scheme = "tokens",
                    credentials = new
                    {
                        token = "playerok-token",
                    },
                },
            });

        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.BadRequest, createResponse.StatusCode);
        Assert.Empty(factory.WorkerControlClient.MarketplaceAuthApplyCalls);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LifecycleMigrate_WithoutTarget_ChoosesBestServerDifferentFromCurrent()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");
        await EnsureActiveAccountTypeAsync(client, "ggsell");

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
        await EnsureActiveAccountTypeAsync(client, "funpay");

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
        await EnsureActiveAccountTypeAsync(client, "platimarket");

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
        await EnsureActiveAccountTypeAsync(client, "platimarket");

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

    [Fact]
    [Trait("Category", "Security")]
    public async Task AccountTypesList_WithoutServiceToken_ReturnsUnauthorized()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/internal/v1/account-types");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AccountTypesList_WithWorkerToken_ReturnsForbidden()
    {
        using var factory = new AccountsManagerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var response = await client.GetAsync("/internal/v1/account-types");
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
        await EnsureActiveAccountTypeAsync(client, platform);

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

    private static async Task EnsureActiveAccountTypeAsync(HttpClient client, string platform)
    {
        var normalizedPlatform = platform.Trim().ToLowerInvariant();
        var accountTypeId = $"it.{normalizedPlatform}";

        using var upsertRequest = CreateMutatingRequest(
            HttpMethod.Put,
            $"/internal/v1/account-types/{accountTypeId}",
            Guid.NewGuid().ToString("N"),
            new
            {
                platform = normalizedPlatform,
                displayName = $"Integration profile: {normalizedPlatform}",
                description = $"Template for integration tests ({normalizedPlatform}).",
                workerProfileId = "it-worker",
                enabled = true,
                sortOrder = 10,
                formFields = new[]
                {
                    new
                    {
                        key = "displayName",
                        label = "Название аккаунта",
                        inputType = "text",
                        required = true,
                        secret = false,
                        placeholder = "Test account",
                        defaultValue = "Test account",
                    },
                },
                runtime = new
                {
                    autospawnEnabled = true,
                    workerImage = "ddcrm/worker-api:local",
                    workerPathPrefix = "/internal/v2/worker",
                    healthPath = "/health",
                    containerPort = 8080,
                    environmentVariables = new
                    {
                        TEST_WORKER_PROVIDER = normalizedPlatform,
                    },
                    workerCommand = new[] { "DDCRM.Worker.Api.dll" },
                },
            });

        var upsertResponse = await client.SendAsync(upsertRequest);
        Assert.Equal(HttpStatusCode.OK, upsertResponse.StatusCode);
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
