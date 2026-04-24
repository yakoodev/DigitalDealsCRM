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
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(userId));

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
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(userId));

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
    public async Task BillingEndpoint_InWave1_ReturnsFeatureNotReady()
    {
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(userId));

        var projectResponse = await SendCreateProjectAsync(client, Guid.NewGuid().ToString("N"), "Gamma");
        using var projectJson = JsonDocument.Parse(await projectResponse.Content.ReadAsStringAsync());
        var projectId = projectJson.RootElement.GetProperty("project").GetProperty("id").GetGuid();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/projects/{projectId}/billing/payments");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        request.Content = JsonContent.Create(new { amount = 1000 });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);

        using var responseJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("FEATURE_NOT_READY", responseJson.RootElement.GetProperty("errorCode").GetString());
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
