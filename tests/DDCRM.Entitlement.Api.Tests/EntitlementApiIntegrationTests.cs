using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDCRM.Entitlement.Api.Tests.Infrastructure;

namespace DDCRM.Entitlement.Api.Tests;

public sealed class EntitlementApiIntegrationTests(EntitlementApiFactory factory) : IClassFixture<EntitlementApiFactory>
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task RecalculateThenCheckAndGet_ReturnsBlockedState()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var projectId = Guid.NewGuid();
        using var recalculateRequest = CreateMutatingRequest(
            "/internal/v1/entitlement/recalculate",
            Guid.NewGuid().ToString("N"),
            new
            {
                projectId,
                subscriptionStatus = "unpaid",
                planKey = "business",
                limits = new
                {
                    accounts = 10,
                },
                addons = new[] { "messages" },
            });

        var recalculateResponse = await client.SendAsync(recalculateRequest);
        Assert.Equal(HttpStatusCode.Accepted, recalculateResponse.StatusCode);

        var checkResponse = await client.PostAsJsonAsync("/internal/v1/entitlement/check", new
        {
            projectId,
            action = "ext.orders.search",
        });

        Assert.Equal(HttpStatusCode.OK, checkResponse.StatusCode);

        using var checkJson = JsonDocument.Parse(await checkResponse.Content.ReadAsStringAsync());
        var checkData = checkJson.RootElement.GetProperty("data");

        Assert.False(checkData.GetProperty("allowed").GetBoolean());
        Assert.Equal("blocked", checkData.GetProperty("state").GetString());

        var getResponse = await client.GetAsync($"/internal/v1/entitlement/{projectId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        using var getJson = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        var getData = getJson.RootElement.GetProperty("data");

        Assert.Equal("blocked", getData.GetProperty("state").GetString());
        Assert.Equal(0, getData.GetProperty("activeOverrides").GetInt32());

        var snapshot = factory.FindSnapshot(projectId);
        Assert.NotNull(snapshot);
        Assert.Equal("blocked", snapshot!.State);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Override_CanAllowSpecificAction_ForBlockedProject()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var projectId = Guid.NewGuid();
        using var recalculateRequest = CreateMutatingRequest(
            "/internal/v1/entitlement/recalculate",
            Guid.NewGuid().ToString("N"),
            new
            {
                projectId,
                subscriptionStatus = "blocked",
            });

        var recalculateResponse = await client.SendAsync(recalculateRequest);
        Assert.Equal(HttpStatusCode.Accepted, recalculateResponse.StatusCode);

        using var overrideRequest = CreateMutatingRequest(
            "/internal/v1/entitlement/override",
            Guid.NewGuid().ToString("N"),
            new
            {
                projectId,
                actor = "ops-admin",
                reason = "support temporary unblock",
                expiresAt = DateTimeOffset.UtcNow.AddHours(1).ToString("O"),
                allowedActions = new[] { "ext.orders.search" },
            });

        var overrideResponse = await client.SendAsync(overrideRequest);
        Assert.Equal(HttpStatusCode.OK, overrideResponse.StatusCode);

        var checkAllowedResponse = await client.PostAsJsonAsync("/internal/v1/entitlement/check", new
        {
            projectId,
            action = "ext.orders.search",
        });

        Assert.Equal(HttpStatusCode.OK, checkAllowedResponse.StatusCode);

        using var checkJson = JsonDocument.Parse(await checkAllowedResponse.Content.ReadAsStringAsync());
        var checkData = checkJson.RootElement.GetProperty("data");
        Assert.True(checkData.GetProperty("allowed").GetBoolean());
        Assert.Equal(1, factory.CountOverrides(projectId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Recalculate_WithSameIdempotencyKey_IsIdempotent()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var projectId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString("N");

        using var firstRequest = CreateMutatingRequest(
            "/internal/v1/entitlement/recalculate",
            idempotencyKey,
            new
            {
                projectId,
                subscriptionStatus = "active",
                planKey = "pro",
            });

        using var secondRequest = CreateMutatingRequest(
            "/internal/v1/entitlement/recalculate",
            idempotencyKey,
            new
            {
                projectId,
                subscriptionStatus = "active",
                planKey = "pro",
            });

        var firstResponse = await client.SendAsync(firstRequest);
        var secondResponse = await client.SendAsync(secondRequest);

        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, secondResponse.StatusCode);

        using var firstJson = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());

        Assert.Equal(
            firstJson.RootElement.GetProperty("requestId").GetString(),
            secondJson.RootElement.GetProperty("requestId").GetString());
        Assert.Equal(1, factory.CountAudits(projectId, "recalculate"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Check_WithoutServiceToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/internal/v1/entitlement/check", new
        {
            projectId = Guid.NewGuid(),
            action = "ext.orders.search",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Check_WithWorkerToken_ReturnsForbidden()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        var response = await client.PostAsJsonAsync("/internal/v1/entitlement/check", new
        {
            projectId = Guid.NewGuid(),
            action = "ext.orders.search",
        });

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
