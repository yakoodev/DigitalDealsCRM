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
    public async Task WorkerHealthAndCapabilities_HappyPath_ReturnsExpectedPayload()
    {
        using var factory = new WorkerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var healthResponse = await client.GetAsync("/internal/v1/worker/health");
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

        using var healthJson = JsonDocument.Parse(await healthResponse.Content.ReadAsStringAsync());
        Assert.Equal("ok", healthJson.RootElement.GetProperty("status").GetString());

        var capabilitiesResponse = await client.GetAsync("/internal/v1/worker/capabilities");
        Assert.Equal(HttpStatusCode.OK, capabilitiesResponse.StatusCode);

        using var capabilitiesJson = JsonDocument.Parse(await capabilitiesResponse.Content.ReadAsStringAsync());
        var capabilities = capabilitiesJson.RootElement.GetProperty("capabilities").EnumerateArray().ToList();
        Assert.Contains(capabilities, x =>
            x.GetProperty("key").GetString() == "ext.market.sync"
            && x.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerListingUpdate_WithSameIdempotencyKey_IsIdempotent()
    {
        using var factory = new WorkerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var idempotencyKey = Guid.NewGuid().ToString("N");
        using var first = CreateMutatingRequest(
            HttpMethod.Patch,
            "/internal/v1/worker/listings/listing-100",
            idempotencyKey,
            new
            {
                title = "Gaming Laptop RTX Pro",
                status = "active",
                price = 1599.99m,
            });

        using var second = CreateMutatingRequest(
            HttpMethod.Patch,
            "/internal/v1/worker/listings/listing-100",
            idempotencyKey,
            new
            {
                title = "Gaming Laptop RTX Pro",
                status = "active",
                price = 1599.99m,
            });

        var firstResponse = await client.SendAsync(first);
        var secondResponse = await client.SendAsync(second);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        using var firstJson = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        Assert.Equal(
            firstJson.RootElement.GetProperty("requestId").GetString(),
            secondJson.RootElement.GetProperty("requestId").GetString());

        var persisted = factory.FindListing("listing-100");
        Assert.NotNull(persisted);
        Assert.Equal("Gaming Laptop RTX Pro", persisted!.Title);
        Assert.Equal(1599.99m, persisted.Price);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerMessageSend_WithSameIdempotencyKey_ReturnsSameMessageId()
    {
        using var factory = new WorkerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var idempotencyKey = Guid.NewGuid().ToString("N");

        using var first = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v1/worker/messages/send",
            idempotencyKey,
            new
            {
                threadId = "thread-1",
                text = "hello from test",
            });

        using var second = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v1/worker/messages/send",
            idempotencyKey,
            new
            {
                threadId = "thread-1",
                text = "hello from test",
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
    public async Task WorkerOrderAction_UnsupportedAction_ReturnsWorkerInvalidAction()
    {
        using var factory = new WorkerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        using var request = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v1/worker/orders/order-100/actions/orders.reopen",
            Guid.NewGuid().ToString("N"),
            new
            {
                payload = new
                {
                    reason = "manual reopen",
                },
            });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("WORKER_INVALID_ACTION", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WorkerExtensionAction_WhenCapabilityMissing_ReturnsConflict()
    {
        using var factory = new WorkerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        using var request = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v1/worker/actions/ext.market.unknown",
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
    public async Task WorkerProxyCredentialsApplyAndReveal_ReturnsStoredCredentials()
    {
        using var factory = new WorkerApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var accountId = Guid.NewGuid();
        var applyIdempotencyKey = Guid.NewGuid().ToString("N");

        using var applyFirst = CreateMutatingRequest(
            HttpMethod.Post,
            "/internal/v1/worker/actions/ext.account.proxy-credentials.apply",
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
            "/internal/v1/worker/actions/ext.account.proxy-credentials.apply",
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
            "/internal/v1/worker/actions/ext.account.proxy-credentials.reveal",
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
    public async Task WorkerScenario_AuthFail_ReturnsWorkerAuthFailed()
    {
        using var factory = new WorkerApiFactory(new Dictionary<string, string?>
        {
            ["TEST_WORKER_SCENARIO"] = WorkerScenarioIds.AuthFail,
            ["TEST_WORKER_CAPABILITY_PROFILE"] = WorkerCapabilityProfiles.FailuresV1,
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var response = await client.GetAsync("/internal/v1/worker/account");
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

        var response = await client.GetAsync("/internal/v1/worker/account");
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

        var first = await client.GetAsync("/internal/v1/worker/account");
        var second = await client.GetAsync("/internal/v1/worker/account");

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
            "/internal/v1/worker/actions/ext.test.capability-mismatch",
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

        var response = await client.PostAsJsonAsync("/internal/v1/worker/listings/search", new { });
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

        var response = await client.GetAsync("/internal/v1/worker/capabilities");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var firstCapability = json.RootElement.GetProperty("capabilities").EnumerateArray().First();
        Assert.False(firstCapability.TryGetProperty("enabled", out _));
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
            "/internal/v1/worker/actions/ext.test.idempotency-replay",
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

        var response = await client.GetAsync("/internal/v1/worker/account");
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

        var response = await client.GetAsync("/internal/v1/worker/account");
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

        var response = await client.GetAsync("/internal/v1/worker/account");
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
