using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DDCRM.Core.Api.Tests.Infrastructure;

namespace DDCRM.Core.Api.Tests;

public sealed class CoreApiIntegrationTests(CoreApiFactory factory) : IClassFixture<CoreApiFactory>
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateProject_ThenListProjects_ReturnsCreatedProject()
    {
        using var client = CreateAuthorizedClient(Guid.NewGuid());

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/projects");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        request.Content = JsonContent.Create(new { name = "Alpha" });

        var createResponse = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var createJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var createdProjectId = createJson.RootElement.GetProperty("project").GetProperty("id").GetGuid();

        var listResponse = await client.GetAsync("/v1/projects");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var items = listJson.RootElement.GetProperty("items");
        Assert.Contains(items.EnumerateArray(), x => x.GetProperty("id").GetGuid() == createdProjectId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateProject_WithSameIdempotencyKey_IsIdempotent()
    {
        using var client = CreateAuthorizedClient(Guid.NewGuid());
        var idempotencyKey = Guid.NewGuid().ToString("N");

        var first = await SendCreateProjectAsync(client, idempotencyKey, "Bravo");
        var second = await SendCreateProjectAsync(client, idempotencyKey, "Bravo");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());

        var firstId = firstJson.RootElement.GetProperty("project").GetProperty("id").GetGuid();
        var secondId = secondJson.RootElement.GetProperty("project").GetProperty("id").GetGuid();

        Assert.Equal(firstId, secondId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task CreateProject_WithoutBearerToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/projects");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        request.Content = JsonContent.Create(new { name = "NoAuth" });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Cors")]
    public async Task PreflightRequest_ReturnsConfiguredCorsHeaders()
    {
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/v1/projects");
        request.Headers.Add("Origin", "https://app.ddcrm.local");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "Authorization,Content-Type");

        var response = await client.SendAsync(request);

        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
        Assert.Contains("https://app.ddcrm.local", values);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateAccount_WithSameIdempotencyKey_IsIdempotent()
    {
        factory.AccountsManagerClient.Reset();

        using var client = CreateAuthorizedClient(Guid.NewGuid());
        var projectId = await CreateProjectAsync(client, "Accounts-A");
        var idempotencyKey = Guid.NewGuid().ToString("N");

        var payload = new
        {
            platform = "ozon",
            displayName = "Store A",
            proxyConfig = new
            {
                host = "proxy-a.internal",
                port = 8080,
                login = "seller-a",
                password = "secret-a",
            },
        };

        var first = await SendJsonAsync(client, HttpMethod.Post, $"/v1/projects/{projectId}/accounts", idempotencyKey, payload);
        var second = await SendJsonAsync(client, HttpMethod.Post, $"/v1/projects/{projectId}/accounts", idempotencyKey, payload);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Single(factory.AccountsManagerClient.CreateCalls);

        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());

        var firstAccount = firstJson.RootElement.GetProperty("account");
        var secondAccount = secondJson.RootElement.GetProperty("account");

        Assert.Equal(firstAccount.GetProperty("id").GetGuid(), secondAccount.GetProperty("id").GetGuid());
        Assert.Equal("ozon", firstAccount.GetProperty("platform").GetString());
        Assert.Equal("Store A", firstAccount.GetProperty("displayName").GetString());
        Assert.True(firstAccount.GetProperty("proxyConfigured").GetBoolean());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateAndDeleteAccount_WorksWithIdempotency()
    {
        factory.AccountsManagerClient.Reset();

        using var client = CreateAuthorizedClient(Guid.NewGuid());
        var projectId = await CreateProjectAsync(client, "Accounts-B");
        var accountId = await CreateAccountAsync(client, projectId, "Store B");

        var updateResponse = await SendJsonAsync(
            client,
            HttpMethod.Patch,
            $"/v1/projects/{projectId}/accounts/{accountId}",
            Guid.NewGuid().ToString("N"),
            new
            {
                displayName = "Store B2",
                businessStatus = "paused",
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Empty(factory.AccountsManagerClient.UpdateCalls);

        using var updateJson = JsonDocument.Parse(await updateResponse.Content.ReadAsStringAsync());
        var account = updateJson.RootElement.GetProperty("account");
        Assert.Equal("Store B2", account.GetProperty("displayName").GetString());
        Assert.Equal("paused", account.GetProperty("businessStatus").GetString());

        var deleteIdempotencyKey = Guid.NewGuid().ToString("N");
        var firstDelete = await SendJsonAsync(
            client,
            HttpMethod.Delete,
            $"/v1/projects/{projectId}/accounts/{accountId}",
            deleteIdempotencyKey,
            payload: null);

        var secondDelete = await SendJsonAsync(
            client,
            HttpMethod.Delete,
            $"/v1/projects/{projectId}/accounts/{accountId}",
            deleteIdempotencyKey,
            payload: null);

        Assert.Equal(HttpStatusCode.OK, firstDelete.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondDelete.StatusCode);
        Assert.Single(factory.AccountsManagerClient.DeleteCalls);

        var listResponse = await client.GetAsync($"/v1/projects/{projectId}/accounts");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.Empty(listJson.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProxyCredentialsMaskedAndUpdate_Workflow()
    {
        factory.AccountsManagerClient.Reset();

        using var client = CreateAuthorizedClient(Guid.NewGuid());
        var projectId = await CreateProjectAsync(client, "Accounts-C");
        var accountId = await CreateAccountAsync(client, projectId, "Store C");

        var maskedResponse = await client.GetAsync($"/v1/projects/{projectId}/accounts/{accountId}/proxy-credentials");
        Assert.Equal(HttpStatusCode.OK, maskedResponse.StatusCode);

        using var maskedJson = JsonDocument.Parse(await maskedResponse.Content.ReadAsStringAsync());
        var masked = maskedJson.RootElement.GetProperty("proxyCredentials");
        Assert.True(masked.GetProperty("configured").GetBoolean());
        Assert.Contains("***", masked.GetProperty("hostMasked").GetString());
        Assert.Contains("***", masked.GetProperty("loginMasked").GetString());

        var updateIdempotencyKey = Guid.NewGuid().ToString("N");
        var firstUpdate = await SendJsonAsync(
            client,
            HttpMethod.Patch,
            $"/v1/projects/{projectId}/accounts/{accountId}/proxy-credentials",
            updateIdempotencyKey,
            new
            {
                reason = "rotating proxy",
                proxyConfig = new
                {
                    host = "proxy-new.internal",
                    port = 8181,
                    login = "seller-c-next",
                    password = "secret-next",
                },
            });

        var secondUpdate = await SendJsonAsync(
            client,
            HttpMethod.Patch,
            $"/v1/projects/{projectId}/accounts/{accountId}/proxy-credentials",
            updateIdempotencyKey,
            new
            {
                reason = "rotating proxy",
                proxyConfig = new
                {
                    host = "proxy-new.internal",
                    port = 8181,
                    login = "seller-c-next",
                    password = "secret-next",
                },
            });

        Assert.Equal(HttpStatusCode.OK, firstUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondUpdate.StatusCode);
        Assert.Single(factory.AccountsManagerClient.UpdateCalls);
        Assert.Equal(accountId, factory.AccountsManagerClient.UpdateCalls[0].AccountId);
        Assert.Equal(8181, Convert.ToInt32(factory.AccountsManagerClient.UpdateCalls[0].ProxyConfig["port"]));
        Assert.Equal(1, factory.CountProxyCredentialsAudits(projectId, accountId));

        var updatedMaskedResponse = await client.GetAsync($"/v1/projects/{projectId}/accounts/{accountId}/proxy-credentials");
        Assert.Equal(HttpStatusCode.OK, updatedMaskedResponse.StatusCode);

        using var updatedMaskedJson = JsonDocument.Parse(await updatedMaskedResponse.Content.ReadAsStringAsync());
        var updatedMasked = updatedMaskedJson.RootElement.GetProperty("proxyCredentials");
        Assert.Contains("***", updatedMasked.GetProperty("hostMasked").GetString());
        Assert.Contains("***", updatedMasked.GetProperty("loginMasked").GetString());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UpdateProxyCredentials_ModeratorRole_IsForbidden()
    {
        factory.AccountsManagerClient.Reset();

        var ownerId = Guid.NewGuid();
        var moderatorId = Guid.NewGuid();

        using var ownerClient = CreateAuthorizedClient(ownerId);
        var projectId = await CreateProjectAsync(ownerClient, "Accounts-D");
        var accountId = await CreateAccountAsync(ownerClient, projectId, "Store D");

        var addMemberResponse = await SendJsonAsync(
            ownerClient,
            HttpMethod.Post,
            $"/v1/projects/{projectId}/members",
            Guid.NewGuid().ToString("N"),
            new { userId = moderatorId, role = "moderator" });
        Assert.Equal(HttpStatusCode.OK, addMemberResponse.StatusCode);

        using var moderatorClient = CreateAuthorizedClient(moderatorId);
        var updateResponse = await SendJsonAsync(
            moderatorClient,
            HttpMethod.Patch,
            $"/v1/projects/{projectId}/accounts/{accountId}/proxy-credentials",
            Guid.NewGuid().ToString("N"),
            new
            {
                reason = "try update",
                proxyConfig = new
                {
                    host = "blocked.internal",
                    port = 8081,
                    login = "blocked",
                    password = "blocked",
                },
            });

        Assert.Equal(HttpStatusCode.Forbidden, updateResponse.StatusCode);
        Assert.Empty(factory.AccountsManagerClient.UpdateCalls);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BillingPayments_ReturnsDataAndIsIdempotent()
    {
        factory.BillingClient.Reset();

        using var client = CreateAuthorizedClient(Guid.NewGuid());
        var projectId = await CreateProjectAsync(client, "Gamma");
        var idempotencyKey = Guid.NewGuid().ToString("N");

        var first = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/projects/{projectId}/billing/payments",
            idempotencyKey,
            new { amount = 1000, currency = "RUB" });

        var second = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/projects/{projectId}/billing/payments",
            idempotencyKey,
            new { amount = 1000, currency = "RUB" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());

        var firstData = firstJson.RootElement.GetProperty("data");
        var secondData = secondJson.RootElement.GetProperty("data");

        Assert.Equal(projectId, firstData.GetProperty("projectId").GetGuid());
        Assert.Equal(firstData.GetProperty("paymentId").GetString(), secondData.GetProperty("paymentId").GetString());
        Assert.Single(factory.BillingClient.CreatePaymentCalls);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BillingPurchaseAddon_ForwardsAddonMetadata()
    {
        factory.BillingClient.Reset();

        using var client = CreateAuthorizedClient(Guid.NewGuid());
        var projectId = await CreateProjectAsync(client, "Delta");

        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/projects/{projectId}/billing/addons/premium/purchase",
            Guid.NewGuid().ToString("N"),
            new { seats = 5 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");

        Assert.Equal("premium", data.GetProperty("addonId").GetString());
        Assert.Equal("addon.purchase", data.GetProperty("operation").GetString());
        Assert.Equal(5, data.GetProperty("seats").GetInt32());
        Assert.Single(factory.BillingClient.CreatePaymentCalls);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BillingChangePlan_ReturnsAckAndIsIdempotent()
    {
        factory.BillingClient.Reset();

        using var client = CreateAuthorizedClient(Guid.NewGuid());
        var projectId = await CreateProjectAsync(client, "Epsilon");
        var idempotencyKey = Guid.NewGuid().ToString("N");

        var first = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/projects/{projectId}/billing/subscription/change-plan",
            idempotencyKey,
            new { planId = "enterprise" });

        var second = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/projects/{projectId}/billing/subscription/change-plan",
            idempotencyKey,
            new { planId = "enterprise" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());

        Assert.Equal("completed", firstJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            firstJson.RootElement.GetProperty("requestId").GetString(),
            secondJson.RootElement.GetProperty("requestId").GetString());
        Assert.Single(factory.BillingClient.ManualActivateCalls);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task BillingPayments_ModeratorRole_IsForbidden()
    {
        factory.BillingClient.Reset();

        var ownerId = Guid.NewGuid();
        var moderatorId = Guid.NewGuid();

        using var ownerClient = CreateAuthorizedClient(ownerId);
        var projectId = await CreateProjectAsync(ownerClient, "Zeta");

        var addMemberResponse = await SendJsonAsync(
            ownerClient,
            HttpMethod.Post,
            $"/v1/projects/{projectId}/members",
            Guid.NewGuid().ToString("N"),
            new { userId = moderatorId, role = "moderator" });

        Assert.Equal(HttpStatusCode.OK, addMemberResponse.StatusCode);

        using var moderatorClient = CreateAuthorizedClient(moderatorId);

        var billingResponse = await SendJsonAsync(
            moderatorClient,
            HttpMethod.Post,
            $"/v1/projects/{projectId}/billing/payments",
            Guid.NewGuid().ToString("N"),
            new { amount = 777 });

        Assert.Equal(HttpStatusCode.Forbidden, billingResponse.StatusCode);
        Assert.Empty(factory.BillingClient.CreatePaymentCalls);
    }

    private HttpClient CreateAuthorizedClient(Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(userId));
        return client;
    }

    private static async Task<Guid> CreateProjectAsync(HttpClient client, string name)
    {
        var response = await SendCreateProjectAsync(client, Guid.NewGuid().ToString("N"), name);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("project").GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient client, Guid projectId, string displayName)
    {
        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/projects/{projectId}/accounts",
            Guid.NewGuid().ToString("N"),
            new
            {
                platform = "ozon",
                displayName,
                proxyConfig = new
                {
                    host = "proxy.initial.internal",
                    port = 8001,
                    login = "seller-initial",
                    password = "secret-initial",
                },
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("account").GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        string idempotencyKey,
        object? payload)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload);
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendCreateProjectAsync(HttpClient client, string idempotencyKey, string name)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/projects")
        {
            Content = JsonContent.Create(new { name })
        };

        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }
}
