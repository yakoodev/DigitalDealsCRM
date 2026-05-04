using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDCRM.Worker.Api.Simulation;
using DDCRM.Worker.Api.Tests.Infrastructure;

namespace DDCRM.Worker.Api.Tests;

public sealed class WorkerApiIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerV2HealthAndCapabilities_HappyPath_ReturnsExpectedPayload()
    {
        using var factory = new WorkerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var healthResponse = await client.GetAsync("/internal/v2/worker/health");
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

        using var healthJson = JsonDocument.Parse(await healthResponse.Content.ReadAsStringAsync());
        Assert.Equal("ok", healthJson.RootElement.GetProperty("status").GetString());

        var capabilitiesResponse = await client.GetAsync("/internal/v2/worker/capabilities");
        Assert.Equal(HttpStatusCode.OK, capabilitiesResponse.StatusCode);

        using var capabilitiesJson = JsonDocument.Parse(await capabilitiesResponse.Content.ReadAsStringAsync());
        Assert.Equal("platimarket", capabilitiesJson.RootElement.GetProperty("provider").GetString());

        var features = capabilitiesJson.RootElement.GetProperty("features");
        Assert.True(features.GetProperty("account.info").GetBoolean());
        Assert.True(features.GetProperty("products.list").GetBoolean());

        var capabilities = capabilitiesJson.RootElement.GetProperty("capabilities").EnumerateArray().ToList();
        Assert.Contains(capabilities, x =>
            x.GetProperty("key").GetString() == "ext.market.sync"
            && x.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerV2AccountInfo_ReturnsExpectedPayload()
    {
        using var factory = new WorkerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var response = await client.GetAsync("/internal/v2/worker/account");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var account = json.RootElement.GetProperty("account");
        Assert.Equal("platimarket", account.GetProperty("provider").GetString());
        Assert.Equal("active", account.GetProperty("status").GetString());

        var raw = account.GetProperty("raw");
        Assert.Equal("test-worker", raw.GetProperty("workerMode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(raw.GetProperty("workerInstanceId").GetString()));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerV2ConversationsList_ReturnsItems()
    {
        using var factory = new WorkerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var response = await client.GetAsync("/internal/v2/worker/conversations?limit=10&onlyUnread=false");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerV2ConversationMessageSend_WithSameIdempotencyKey_IsIdempotent()
    {
        using var factory = new WorkerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var listResponse = await client.GetAsync("/internal/v2/worker/conversations?limit=10");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var conversationId = listJson.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .First()
            .GetProperty("conversationId")
            .GetString();
        Assert.False(string.IsNullOrWhiteSpace(conversationId));

        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var first = CreateMutatingRequest(
            HttpMethod.Post,
            $"/internal/v2/worker/conversations/{conversationId}/messages",
            idempotencyKey,
            new
            {
                text = "test v2 message",
                attachments = new[]
                {
                    new { type = "text", url = (string?)null },
                },
            });

        using var second = CreateMutatingRequest(
            HttpMethod.Post,
            $"/internal/v2/worker/conversations/{conversationId}/messages",
            idempotencyKey,
            new
            {
                text = "test v2 message",
                attachments = new[]
                {
                    new { type = "text", url = (string?)null },
                },
            });

        var firstResponse = await client.SendAsync(first);
        var secondResponse = await client.SendAsync(second);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        using var firstJson = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            firstJson.RootElement.GetProperty("messageId").GetString(),
            secondJson.RootElement.GetProperty("messageId").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerV2Products_UpdateWithStaleVersion_ReturnsConflict()
    {
        using var factory = new WorkerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var createKey = Guid.NewGuid().ToString("N");
        using var createRequest = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v2/worker/products",
            createKey,
            new
            {
                schemaId = "digital_goods.v1",
                title = "V2 Test Product",
                description = "integration test",
                price = new { amount = 99.99m, currency = "rub" },
                quantity = 3,
                attributes = new { region = "RU" },
            });

        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        using var createJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var productId = createJson.RootElement.GetProperty("productId").GetString();
        var createdVersion = createJson.RootElement.GetProperty("version").GetString();
        Assert.False(string.IsNullOrWhiteSpace(productId));
        Assert.False(string.IsNullOrWhiteSpace(createdVersion));

        using var updateRequest = CreateMutatingRequest(
            HttpMethod.Patch,
            $"/internal/v2/worker/products/{productId}",
            Guid.NewGuid().ToString("N"),
            new
            {
                expectedVersion = createdVersion,
                changes = new
                {
                    price = new { amount = 89.99m, currency = "RUB" },
                },
            });

        var updateResponse = await client.SendAsync(updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        using var staleUpdateRequest = CreateMutatingRequest(
            HttpMethod.Patch,
            $"/internal/v2/worker/products/{productId}",
            Guid.NewGuid().ToString("N"),
            new
            {
                expectedVersion = createdVersion,
                changes = new
                {
                    status = "active",
                },
            });

        var staleUpdateResponse = await client.SendAsync(staleUpdateRequest);
        Assert.Equal(HttpStatusCode.Conflict, staleUpdateResponse.StatusCode);

        using var staleJson = JsonDocument.Parse(await staleUpdateResponse.Content.ReadAsStringAsync());
        Assert.Equal("WORKER_RUNTIME_CONFLICT", staleJson.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerV2ProductsCreate_WithUnknownSchema_ReturnsValidationError()
    {
        using var factory = new WorkerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        using var request = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v2/worker/products",
            Guid.NewGuid().ToString("N"),
            new
            {
                schemaId = "unknown.schema.v1",
                title = "Unknown Schema Product",
                price = new { amount = 100m, currency = "RUB" },
            });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("WORKER_PLATFORM_ERROR", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerV2ProductsCreate_PlayerokSchemaWithoutRequiredAttribute_ReturnsValidationError()
    {
        using var factory = new WorkerApiFactory(new Dictionary<string, string?>
        {
            ["TEST_WORKER_PROVIDER"] = "playerok",
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        using var request = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v2/worker/products",
            Guid.NewGuid().ToString("N"),
            new
            {
                schemaId = "playerok.item.v1",
                title = "Playerok Product",
                price = new { amount = 150m, currency = "RUB" },
                attributes = new { obtainingTypeId = "gift" },
            });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("WORKER_PLATFORM_ERROR", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerV2ProductsCreate_PlayerokSchemaWithRequiredAttribute_Succeeds()
    {
        using var factory = new WorkerApiFactory(new Dictionary<string, string?>
        {
            ["TEST_WORKER_PROVIDER"] = "playerok",
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        using var request = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v2/worker/products",
            Guid.NewGuid().ToString("N"),
            new
            {
                schemaId = "playerok.item.v1",
                title = "Playerok Product",
                price = new { amount = 150m, currency = "RUB" },
                attributes = new { gameCategoryId = "boosting", obtainingTypeId = "gift" },
            });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("draft", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerV2ProductSchemas_ReturnsItems()
    {
        using var factory = new WorkerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var response = await client.GetAsync("/internal/v2/worker/schemas/products");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        Assert.Contains(items, item => item.GetProperty("schemaId").GetString() == "digital_goods.v1");
        Assert.All(items, item => Assert.Equal("platimarket", item.GetProperty("provider").GetString()));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerV2ProviderProfile_FunPay_AppliesToAccountCapabilitiesAndSchemas()
    {
        using var factory = new WorkerApiFactory(new Dictionary<string, string?>
        {
            ["TEST_WORKER_PROVIDER"] = "funpay",
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var capabilitiesResponse = await client.GetAsync("/internal/v2/worker/capabilities");
        Assert.Equal(HttpStatusCode.OK, capabilitiesResponse.StatusCode);
        using var capabilitiesJson = JsonDocument.Parse(await capabilitiesResponse.Content.ReadAsStringAsync());
        Assert.Equal("funpay", capabilitiesJson.RootElement.GetProperty("provider").GetString());

        var accountResponse = await client.GetAsync("/internal/v2/worker/account");
        Assert.Equal(HttpStatusCode.OK, accountResponse.StatusCode);
        using var accountJson = JsonDocument.Parse(await accountResponse.Content.ReadAsStringAsync());
        var account = accountJson.RootElement.GetProperty("account");
        Assert.Equal("funpay", account.GetProperty("provider").GetString());
        Assert.Equal("ddcrm_funpay_demo", account.GetProperty("nickname").GetString());

        var schemasResponse = await client.GetAsync("/internal/v2/worker/schemas/products");
        Assert.Equal(HttpStatusCode.OK, schemasResponse.StatusCode);
        using var schemasJson = JsonDocument.Parse(await schemasResponse.Content.ReadAsStringAsync());
        var schemaIds = schemasJson.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(x => x.GetProperty("schemaId").GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("funpay.item.v1", schemaIds);
        Assert.DoesNotContain("platimarket.item.v1", schemaIds);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerV2ProviderProfile_GgSell_DisablesUnsupportedOperationsAtRuntime()
    {
        using var factory = new WorkerApiFactory(new Dictionary<string, string?>
        {
            ["TEST_WORKER_PROVIDER"] = "ggsell",
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var capabilitiesResponse = await client.GetAsync("/internal/v2/worker/capabilities");
        Assert.Equal(HttpStatusCode.OK, capabilitiesResponse.StatusCode);
        using var capabilitiesJson = JsonDocument.Parse(await capabilitiesResponse.Content.ReadAsStringAsync());
        Assert.Equal("ggsell", capabilitiesJson.RootElement.GetProperty("provider").GetString());

        var features = capabilitiesJson.RootElement.GetProperty("features");
        Assert.False(features.GetProperty("conversations.list").GetBoolean());
        Assert.False(features.GetProperty("products.create").GetBoolean());
        Assert.True(features.GetProperty("products.list").GetBoolean());

        var accountResponse = await client.GetAsync("/internal/v2/worker/account");
        Assert.Equal(HttpStatusCode.OK, accountResponse.StatusCode);

        var conversationsResponse = await client.GetAsync("/internal/v2/worker/conversations?limit=5");
        Assert.Equal(HttpStatusCode.Conflict, conversationsResponse.StatusCode);
        using (var conversationsJson = JsonDocument.Parse(await conversationsResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal("WORKER_RUNTIME_CONFLICT", conversationsJson.RootElement.GetProperty("errorCode").GetString());
        }

        using var createRequest = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v2/worker/products",
            Guid.NewGuid().ToString("N"),
            new
            {
                schemaId = "digital_goods.v1",
                title = "Disabled by feature map",
                description = "should fail for ggsell profile",
                price = new { amount = 99m, currency = "RUB" },
                quantity = 1,
                attributes = new { region = "RU" },
            });

        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Conflict, createResponse.StatusCode);
        using (var createJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal("WORKER_RUNTIME_CONFLICT", createJson.RootElement.GetProperty("errorCode").GetString());
        }

        var productsListResponse = await client.GetAsync("/internal/v2/worker/products?limit=5");
        Assert.Equal(HttpStatusCode.OK, productsListResponse.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerV2ProductSchemas_WithInvalidProviderQuery_ReturnsValidationError()
    {
        using var factory = new WorkerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var response = await client.GetAsync("/internal/v2/worker/schemas/products?provider=unknown-market");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("WORKER_PLATFORM_ERROR", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerV2ExtensionAction_WhenCapabilityMissing_ReturnsConflict()
    {
        using var factory = new WorkerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        using var request = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v2/worker/actions/ext.market.unknown",
            Guid.NewGuid().ToString("N"),
            new
            {
                payload = new
                {
                    test = true,
                },
            });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("WORKER_RUNTIME_CONFLICT", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerV2ProxyCredentialsApplyAndReveal_ReturnsStoredCredentials()
    {
        using var factory = new WorkerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var accountId = Guid.NewGuid();
        var applyIdempotencyKey = Guid.NewGuid().ToString("N");

        using var applyFirst = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v2/worker/actions/ext.account.proxy-credentials.apply",
            applyIdempotencyKey,
            new
            {
                payload = new
                {
                    accountId,
                    proxyConfig = new
                    {
                        host = "proxy.worker.internal",
                        port = 8181,
                        login = "worker-login",
                        password = "worker-secret",
                    },
                },
            });

        using var applySecond = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v2/worker/actions/ext.account.proxy-credentials.apply",
            applyIdempotencyKey,
            new
            {
                payload = new
                {
                    accountId,
                    proxyConfig = new
                    {
                        host = "proxy.worker.internal",
                        port = 8181,
                        login = "worker-login",
                        password = "worker-secret",
                    },
                },
            });

        var applyFirstResponse = await client.SendAsync(applyFirst);
        var applySecondResponse = await client.SendAsync(applySecond);

        Assert.Equal(HttpStatusCode.OK, applyFirstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, applySecondResponse.StatusCode);

        var stored = factory.FindProxyCredentials(accountId);
        Assert.NotNull(stored);
        Assert.Equal("proxy.worker.internal", stored!.Host);
        Assert.Equal(8181, stored.Port);
        Assert.Equal("worker-login", stored.Login);
        Assert.NotEqual("worker-secret", stored.Password);

        using var revealRequest = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v2/worker/actions/ext.account.proxy-credentials.reveal",
            Guid.NewGuid().ToString("N"),
            new
            {
                payload = new
                {
                    accountId,
                    reason = "audit support",
                },
            });

        var revealResponse = await client.SendAsync(revealRequest);
        Assert.Equal(HttpStatusCode.OK, revealResponse.StatusCode);

        using var revealJson = JsonDocument.Parse(await revealResponse.Content.ReadAsStringAsync());
        var proxyConfig = revealJson.RootElement.GetProperty("result").GetProperty("proxyConfig");
        Assert.Equal("proxy.worker.internal", proxyConfig.GetProperty("host").GetString());
        Assert.Equal(8181, proxyConfig.GetProperty("port").GetInt32());
        Assert.Equal("worker-login", proxyConfig.GetProperty("login").GetString());
        Assert.Equal("worker-secret", proxyConfig.GetProperty("password").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerV2MarketplaceAuthApply_StoresEncryptedMarketplaceAuth()
    {
        using var factory = new WorkerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var accountId = Guid.NewGuid();
        using var request = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v2/worker/actions/ext.account.marketplace-auth.apply",
            Guid.NewGuid().ToString("N"),
            new
            {
                payload = new
                {
                    accountId,
                    marketplaceAuth = new
                    {
                        scheme = "golden_key",
                        credentials = new
                        {
                            golden_key = "funpay-golden-key",
                            user_agent = "Mozilla/5.0",
                        },
                    },
                },
            });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = factory.FindMarketplaceAuth(accountId);
        Assert.NotNull(stored);
        Assert.Equal("golden_key", stored!.Scheme);
        Assert.NotEqual("funpay-golden-key", stored.CredentialsEncrypted);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerScenario_AuthFail_ReturnsWorkerAuthFailed()
    {
        using var factory = new WorkerApiFactory(new Dictionary<string, string?>
        {
            ["TEST_WORKER_SCENARIO"] = WorkerScenarioIds.AuthFail,
            ["TEST_WORKER_CAPABILITY_PROFILE"] = WorkerCapabilityProfiles.FailuresV1,
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var response = await client.GetAsync("/internal/v2/worker/account");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("WORKER_AUTH_FAILED", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerScenario_Timeout_ReturnsWorkerUnavailable()
    {
        using var factory = new WorkerApiFactory(new Dictionary<string, string?>
        {
            ["TEST_WORKER_SCENARIO"] = WorkerScenarioIds.Timeout,
            ["TEST_WORKER_CAPABILITY_PROFILE"] = WorkerCapabilityProfiles.FailuresV1,
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var response = await client.GetAsync("/internal/v2/worker/account");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("WORKER_UNAVAILABLE", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerScenario_TransientError_FailsThenRecovers()
    {
        using var factory = new WorkerApiFactory(new Dictionary<string, string?>
        {
            ["TEST_WORKER_SCENARIO"] = WorkerScenarioIds.TransientError,
            ["TEST_WORKER_CAPABILITY_PROFILE"] = WorkerCapabilityProfiles.FailuresV1,
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var first = await client.GetAsync("/internal/v2/worker/account");
        var second = await client.GetAsync("/internal/v2/worker/account");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerScenario_CapabilityMismatch_ReturnsConflict()
    {
        using var factory = new WorkerApiFactory(new Dictionary<string, string?>
        {
            ["TEST_WORKER_SCENARIO"] = WorkerScenarioIds.CapabilityMismatch,
            ["TEST_WORKER_CAPABILITY_PROFILE"] = WorkerCapabilityProfiles.ContractV1,
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        using var request = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v2/worker/actions/ext.test.capability-mismatch",
            Guid.NewGuid().ToString("N"),
            new { payload = new { mode = "strict" } });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("WORKER_RUNTIME_CONFLICT", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerScenario_MalformedPayload_ReturnsIntentionalMalformedResponse()
    {
        using var factory = new WorkerApiFactory(new Dictionary<string, string?>
        {
            ["TEST_WORKER_SCENARIO"] = WorkerScenarioIds.MalformedPayload,
            ["TEST_WORKER_CAPABILITY_PROFILE"] = WorkerCapabilityProfiles.ContractV1,
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var response = await client.GetAsync("/internal/v2/worker/conversations?limit=5");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("items").ValueKind);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerScenario_ContractDrift_CapabilitiesPayloadIsDrifted()
    {
        using var factory = new WorkerApiFactory(new Dictionary<string, string?>
        {
            ["TEST_WORKER_SCENARIO"] = WorkerScenarioIds.ContractDrift,
            ["TEST_WORKER_CAPABILITY_PROFILE"] = WorkerCapabilityProfiles.ContractV1,
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var response = await client.GetAsync("/internal/v2/worker/capabilities");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var firstCapability = json.RootElement.GetProperty("capabilities").EnumerateArray().First();
        Assert.False(firstCapability.TryGetProperty("enabled", out _));
        Assert.False(json.RootElement.TryGetProperty("features", out _));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerExtensionAction_WhenExtTestDisabled_ReturnsForbidden()
    {
        using var factory = new WorkerApiFactory(new Dictionary<string, string?>
        {
            ["TEST_WORKER_CAPABILITY_PROFILE"] = WorkerCapabilityProfiles.ContractV1,
            ["TEST_WORKER_EXT_ACTIONS_ENABLED"] = "false",
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        using var request = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v2/worker/actions/ext.test.idempotency-replay",
            Guid.NewGuid().ToString("N"),
            new { });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("WORKER_INVALID_ACTION", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task WorkerApi_WithoutServiceToken_ReturnsUnauthorized()
    {
        using var factory = new WorkerApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/internal/v2/worker/account");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("WORKER_AUTH_FAILED", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task WorkerApi_WithInvalidServiceToken_ReturnsUnauthorized()
    {
        using var factory = new WorkerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "invalid-token");

        var response = await client.GetAsync("/internal/v2/worker/account");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("WORKER_AUTH_FAILED", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task WorkerApi_WithInternalToken_ReturnsForbidden()
    {
        using var factory = new WorkerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var response = await client.GetAsync("/internal/v2/worker/account");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("WORKER_AUTH_FAILED", json.RootElement.GetProperty("errorCode").GetString());
    }

    private static HttpRequestMessage CreateMutatingRequest(HttpMethod method, string path, string idempotencyKey, object payload)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }
}
