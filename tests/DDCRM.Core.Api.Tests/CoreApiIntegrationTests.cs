using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DDCRM.Core.Api.AccountsManager;
using DDCRM.Core.Api.Tests.Infrastructure;

namespace DDCRM.Core.Api.Tests;

public sealed class CoreApiIntegrationTests(CoreApiFactory factory) : IClassFixture<CoreApiFactory>
{
    [Fact]
    [Trait("Category", "Auth")]
    public async Task AuthRegister_ThenLogin_Works()
    {
        using var client = factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@ddcrm.local";
        const string password = "Passw0rd!123";

        var registerResponse = await client.PostAsJsonAsync(
            "/v1/auth/register",
            new
            {
                email,
                password,
                displayName = "Integration User",
            });

        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        using (var registerJson = JsonDocument.Parse(await registerResponse.Content.ReadAsStringAsync()))
        {
            Assert.False(string.IsNullOrWhiteSpace(registerJson.RootElement.GetProperty("token").GetString()));
            Assert.Equal(email, registerJson.RootElement.GetProperty("user").GetProperty("email").GetString());
            Assert.False(registerJson.RootElement.GetProperty("user").GetProperty("requiresPasswordChange").GetBoolean());
        }

        var loginResponse = await client.PostAsJsonAsync(
            "/v1/auth/login",
            new
            {
                email,
                password,
            });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        using var loginJson = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(loginJson.RootElement.GetProperty("token").GetString()));
        Assert.Equal(email, loginJson.RootElement.GetProperty("user").GetProperty("email").GetString());
        Assert.False(loginJson.RootElement.GetProperty("user").GetProperty("requiresPasswordChange").GetBoolean());
    }

    [Fact]
    [Trait("Category", "Auth")]
    public async Task SuperAdmin_Login_RequiresPasswordChange_ThenUnlocksProtectedEndpoints()
    {
        using var client = factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync(
            "/v1/auth/login",
            new
            {
                email = CoreApiFactory.SuperAdminEmail,
                password = CoreApiFactory.SuperAdminPassword,
            });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        using var loginJson = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        var token = loginJson.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.True(loginJson.RootElement.GetProperty("user").GetProperty("requiresPasswordChange").GetBoolean());

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var blockedResponse = await client.GetAsync("/v1/projects");
        Assert.Equal(HttpStatusCode.Forbidden, blockedResponse.StatusCode);
        using (var blockedJson = JsonDocument.Parse(await blockedResponse.Content.ReadAsStringAsync()))
        {
            var details = blockedJson.RootElement.GetProperty("details");
            Assert.True(details.GetProperty("passwordChangeRequired").GetBoolean());
        }

        var newPassword = $"N3wPass!{Guid.NewGuid():N}".Substring(0, 20);
        var changeResponse = await client.PostAsJsonAsync(
            "/v1/auth/change-password",
            new
            {
                currentPassword = CoreApiFactory.SuperAdminPassword,
                newPassword,
            });
        Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);

        var projectsResponse = await client.GetAsync("/v1/projects");
        Assert.Equal(HttpStatusCode.OK, projectsResponse.StatusCode);
    }

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
    public async Task CreateAccount_WithMarketplaceAuth_ForwardsPayloadToAccountsManager()
    {
        factory.AccountsManagerClient.Reset();
        SeedAccountType("test-worker.funpay", "funpay");

        using var client = CreateAuthorizedClient(Guid.NewGuid());
        var projectId = await CreateProjectAsync(client, "Accounts-MarketplaceAuth");
        var idempotencyKey = Guid.NewGuid().ToString("N");

        var payload = new
        {
            platform = "funpay",
            accountTypeId = "test-worker.funpay",
            displayName = "FunPay Auth Account",
            proxyConfig = new
            {
                host = "proxy-auth.internal",
                port = 1508,
                login = "seller-auth",
                password = "proxy-secret",
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
        };

        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/projects/{projectId}/accounts",
            idempotencyKey,
            payload);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var createCall = Assert.Single(factory.AccountsManagerClient.CreateCalls);
        Assert.NotNull(createCall.MarketplaceAuth);
        Assert.Equal("golden_key", createCall.MarketplaceAuth!.Scheme);
        Assert.Equal("funpay-golden-key", createCall.MarketplaceAuth.Credentials["golden_key"]);
        Assert.Equal("Mozilla/5.0", createCall.MarketplaceAuth.Credentials["user_agent"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateAccount_WithMarketplaceAuthTokensWithoutDdg5_ReturnsBadRequest()
    {
        factory.AccountsManagerClient.Reset();
        SeedAccountType("test-worker.playerok", "playerok");

        using var client = CreateAuthorizedClient(Guid.NewGuid());
        var projectId = await CreateProjectAsync(client, "Accounts-PlayerokAuthValidation");
        var idempotencyKey = Guid.NewGuid().ToString("N");

        var payload = new
        {
            platform = "playerok",
            accountTypeId = "test-worker.playerok",
            displayName = "Playerok Auth Account",
            proxyConfig = new
            {
                host = "proxy-auth.internal",
                port = 1508,
                login = "seller-auth",
                password = "proxy-secret",
            },
            marketplaceAuth = new
            {
                scheme = "tokens",
                credentials = new
                {
                    token = "playerok-token",
                },
            },
        };

        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/projects/{projectId}/accounts",
            idempotencyKey,
            payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.AccountsManagerClient.CreateCalls);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListProjectAccountTypes_ReturnsAccountsManagerCatalog()
    {
        factory.AccountsManagerClient.Reset();
        SeedAccountType("it.funpay", "funpay");
        SeedAccountType("it.playerok", "playerok");

        using var client = CreateAuthorizedClient(Guid.NewGuid());
        var projectId = await CreateProjectAsync(client, "AccountTypes-A");

        var response = await client.GetAsync($"/v1/projects/{projectId}/account-types");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .ToArray();

        Assert.Equal(2, items.Length);

        var accountTypeIds = items
            .Select(x => x.GetProperty("accountTypeId").GetString() ?? string.Empty)
            .ToArray();
        Assert.Equal(
            ["it.funpay", "it.playerok"],
            accountTypeIds);

        foreach (var item in items)
        {
            Assert.Equal("it-worker", item.GetProperty("workerProfileId").GetString());
            Assert.True(item.GetProperty("enabled").GetBoolean());
            Assert.True(item.GetProperty("formFields").GetArrayLength() >= 1);
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AdminAccountManagerEndpoints_WithoutSystemPermission_AreForbidden()
    {
        using var client = CreateAuthorizedClient(Guid.NewGuid(), withSystemPermission: false);

        var response = await client.GetAsync("/v1/admin/account-manager/account-types");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AdminIntegrationGrant_WithoutSystemPermission_IsForbidden()
    {
        using var client = CreateAuthorizedClient(Guid.NewGuid(), withSystemPermission: false);
        var projectId = await CreateProjectAsync(client, "Integrations-NoAdmin");

        var response = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/v1/admin/integrations/projects/{projectId}/grants/platform.funpay",
            Guid.NewGuid().ToString("N"),
            new
            {
                scopes = new[] { "use" },
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminIntegrationGrant_UpsertAndList_Works()
    {
        var userId = Guid.NewGuid();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.CreateToken(userId, "system.integrations.manage"));

        var projectId = await CreateProjectAsync(client, "Integrations-Admin");

        var upsertResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            $"/v1/admin/integrations/projects/{projectId}/grants/funpaystat",
            Guid.NewGuid().ToString("N"),
            new
            {
                scopes = new[] { "read", "jobs" },
            });
        Assert.Equal(HttpStatusCode.OK, upsertResponse.StatusCode);

        var listResponse = await client.GetAsync($"/v1/admin/integrations/projects/{projectId}/grants");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var items = listJson.RootElement.GetProperty("items");
        Assert.Contains(items.EnumerateArray(), item =>
            string.Equals(item.GetProperty("integrationKey").GetString(), "funpaystat", StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.GetProperty("status").GetString(), "active", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminAccountManagerEndpoints_WithSystemPermission_Work()
    {
        factory.AccountsManagerClient.Reset();
        using var client = CreateAuthorizedClient(Guid.NewGuid(), withSystemPermission: true);

        var serversResponse = await client.GetAsync("/v1/admin/account-manager/worker-servers");
        Assert.Equal(HttpStatusCode.OK, serversResponse.StatusCode);

        var serverUpsertResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/v1/admin/account-manager/worker-servers/srv-admin-a",
            Guid.NewGuid().ToString("N"),
            new
            {
                baseUrlTemplate = "http://{workerId}:{workerPort}",
                status = "active",
                health = "healthy",
                capacity = 20,
                currentLoad = 1,
                dockerHost = "unix:///var/run/docker.sock",
                dockerNetwork = "ddcrm_ddcrm",
                registry = new
                {
                    enabled = true,
                    host = "ghcr.io",
                    username = "demo-user",
                    token = "ghp_demo_token",
                },
                metadata = new
                {
                    region = "eu",
                },
            });
        Assert.Equal(HttpStatusCode.OK, serverUpsertResponse.StatusCode);
        Assert.Single(factory.AccountsManagerClient.UpsertWorkerServerCalls);
        using (var workerServerJson = JsonDocument.Parse(await serverUpsertResponse.Content.ReadAsStringAsync()))
        {
            var registry = workerServerJson.RootElement
                .GetProperty("workerServer")
                .GetProperty("registry");
            Assert.True(registry.GetProperty("enabled").GetBoolean());
            Assert.Equal("ghcr.io", registry.GetProperty("host").GetString());
            Assert.Equal("demo-user", registry.GetProperty("username").GetString());
            Assert.True(registry.GetProperty("hasToken").GetBoolean());
            Assert.False(registry.TryGetProperty("token", out _));
        }

        var upsertCall = factory.AccountsManagerClient.UpsertWorkerServerCalls.Single();
        Assert.Equal("ghp_demo_token", upsertCall.Input.Registry?.Token);

        var accountTypesResponse = await client.GetAsync("/v1/admin/account-manager/account-types");
        Assert.Equal(HttpStatusCode.OK, accountTypesResponse.StatusCode);

        var accountTypeUpsertResponse = await SendJsonAsync(
            client,
            HttpMethod.Put,
            "/v1/admin/account-manager/account-types/test-worker.funpay",
            Guid.NewGuid().ToString("N"),
            new
            {
                platform = "funpay",
                displayName = "FunPay runtime profile",
                description = "Updated by admin",
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
        Assert.Equal(HttpStatusCode.OK, accountTypeUpsertResponse.StatusCode);
        Assert.Single(factory.AccountsManagerClient.UpsertAccountTypeCalls);
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
    [Trait("Category", "Integration")]
    public async Task RevealProxyCredentials_ReturnsProxyConfig_AndAuditsIdempotently()
    {
        factory.GatewayProxyClient.Reset();

        using var client = CreateAuthorizedClient(Guid.NewGuid());
        var projectId = await CreateProjectAsync(client, "Accounts-Reveal-A");
        var accountId = await CreateAccountAsync(client, projectId, "Store Reveal A");
        var idempotencyKey = Guid.NewGuid().ToString("N");

        var first = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/projects/{projectId}/accounts/{accountId}/proxy-credentials/reveal",
            idempotencyKey,
            new
            {
                reason = "support audit",
            });

        var second = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/projects/{projectId}/accounts/{accountId}/proxy-credentials/reveal",
            idempotencyKey,
            new
            {
                reason = "support audit",
            });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(2, factory.GatewayProxyClient.Calls.Count);
        Assert.All(factory.GatewayProxyClient.Calls, call =>
        {
            Assert.Equal("ext.account.proxy-credentials.reveal", call.Action);
            Assert.Equal(idempotencyKey, call.IdempotencyKey);
        });

        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());

        var firstProxyConfig = firstJson.RootElement.GetProperty("proxyConfig");
        var secondProxyConfig = secondJson.RootElement.GetProperty("proxyConfig");

        Assert.Equal("proxy.reveal.internal", firstProxyConfig.GetProperty("host").GetString());
        Assert.Equal(8443, firstProxyConfig.GetProperty("port").GetInt32());
        Assert.Equal("reveal-login", firstProxyConfig.GetProperty("login").GetString());
        Assert.StartsWith("secret-", firstProxyConfig.GetProperty("password").GetString()!, StringComparison.Ordinal);
        Assert.Equal(firstProxyConfig.GetRawText(), secondProxyConfig.GetRawText());
        Assert.Equal(1, factory.CountProxyCredentialsAudits(projectId, accountId));
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
    [Trait("Category", "Security")]
    public async Task RevealProxyCredentials_ModeratorRole_IsForbidden()
    {
        factory.GatewayProxyClient.Reset();

        var ownerId = Guid.NewGuid();
        var moderatorId = Guid.NewGuid();

        using var ownerClient = CreateAuthorizedClient(ownerId);
        var projectId = await CreateProjectAsync(ownerClient, "Accounts-Reveal-B");
        var accountId = await CreateAccountAsync(ownerClient, projectId, "Store Reveal B");

        var addMemberResponse = await SendJsonAsync(
            ownerClient,
            HttpMethod.Post,
            $"/v1/projects/{projectId}/members",
            Guid.NewGuid().ToString("N"),
            new { userId = moderatorId, role = "moderator" });
        Assert.Equal(HttpStatusCode.OK, addMemberResponse.StatusCode);

        using var moderatorClient = CreateAuthorizedClient(moderatorId);
        var revealResponse = await SendJsonAsync(
            moderatorClient,
            HttpMethod.Post,
            $"/v1/projects/{projectId}/accounts/{accountId}/proxy-credentials/reveal",
            Guid.NewGuid().ToString("N"),
            new
            {
                reason = "need reveal",
            });

        Assert.Equal(HttpStatusCode.Forbidden, revealResponse.StatusCode);
        Assert.Empty(factory.GatewayProxyClient.Calls);
        Assert.Equal(0, factory.CountProxyCredentialsAudits(projectId, accountId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProxyAccountApiAction_ReturnsResultAndIsIdempotent()
    {
        factory.GatewayProxyClient.Reset();

        using var client = CreateAuthorizedClient(Guid.NewGuid());
        var idempotencyKey = Guid.NewGuid().ToString("N");
        var payload = new
        {
            conversationId = "conv-100",
            text = "hello from core-test",
            onlyUnread = false,
        };

        var first = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/v1/account-api/rk.alpha/conversations.messages.send",
            idempotencyKey,
            payload);

        var second = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/v1/account-api/rk.alpha/conversations.messages.send",
            idempotencyKey,
            payload);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Single(factory.GatewayProxyClient.Calls);

        var call = factory.GatewayProxyClient.Calls[0];
        Assert.Equal("rk.alpha", call.RouteKey);
        Assert.Equal("conversations.messages.send", call.Action);
        Assert.Equal(idempotencyKey, call.IdempotencyKey);
        Assert.StartsWith("Bearer ", call.AuthorizationHeader, StringComparison.Ordinal);

        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());

        var firstResult = firstJson.RootElement.GetProperty("result");
        var secondResult = secondJson.RootElement.GetProperty("result");

        Assert.Equal("rk.alpha", firstResult.GetProperty("routeKey").GetString());
        Assert.Equal("conversations.messages.send", firstResult.GetProperty("action").GetString());
        Assert.Equal(idempotencyKey, firstResult.GetProperty("idempotencyKey").GetString());

        var firstEcho = firstResult.GetProperty("echo");
        Assert.Equal(payload.conversationId, firstEcho.GetProperty("conversationId").GetString());
        Assert.Equal(payload.text, firstEcho.GetProperty("text").GetString());
        Assert.Equal(payload.onlyUnread, firstEcho.GetProperty("onlyUnread").GetBoolean());

        Assert.Equal(firstResult.GetRawText(), secondResult.GetRawText());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ProxyAccountApiAction_SensitiveExtAccountAction_IsRejected()
    {
        factory.GatewayProxyClient.Reset();

        using var client = CreateAuthorizedClient(Guid.NewGuid());

        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/v1/account-api/rk.alpha/ext.account.proxy-credentials.reveal",
            Guid.NewGuid().ToString("N"),
            new
            {
                accountId = Guid.NewGuid(),
                reason = "audit request",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.GatewayProxyClient.Calls);
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

    private HttpClient CreateAuthorizedClient(Guid userId, bool withSystemPermission = false)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.CreateToken(userId, withSystemPermission));
        return client;
    }

    private async Task<Guid> CreateProjectAsync(HttpClient client, string name)
    {
        var response = await SendCreateProjectAsync(client, Guid.NewGuid().ToString("N"), name);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var projectId = json.RootElement.GetProperty("project").GetProperty("id").GetGuid();

        factory.GrantProjectIntegration(projectId, "platform.ozon", "use");
        factory.GrantProjectIntegration(projectId, "platform.funpay", "use");
        factory.GrantProjectIntegration(projectId, "platform.playerok", "use");
        factory.GrantProjectIntegration(projectId, "platform.ggsell", "use");
        factory.GrantProjectIntegration(projectId, "platform.platimarket", "use");

        return projectId;
    }

    private void SeedAccountType(string accountTypeId, string platform)
    {
        factory.AccountsManagerClient.AccountTypes.RemoveAll(item =>
            string.Equals(item.AccountTypeId, accountTypeId, StringComparison.OrdinalIgnoreCase));

        factory.AccountsManagerClient.AccountTypes.Add(
            new AccountsManagerAccountTypeDefinition(
                accountTypeId,
                platform,
                $"Integration template: {platform}",
                $"Template for integration tests ({platform}).",
                "it-worker",
                true,
                10,
                [
                    new AccountsManagerAccountTypeField(
                        "displayName",
                        "Название аккаунта",
                        "text",
                        true,
                        false,
                        "Test account",
                        "Test account"),
                ],
                new AccountsManagerAccountTypeRuntime(
                    true,
                    "ddcrm/worker-api:local",
                    "/internal/v2/worker",
                    "/health",
                    8080,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["TEST_WORKER_PROVIDER"] = platform,
                    },
                    ["DDCRM.Worker.Api.dll"])));
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
