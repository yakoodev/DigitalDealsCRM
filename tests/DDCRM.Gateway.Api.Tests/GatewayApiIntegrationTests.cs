using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DDCRM.Gateway.Api.Clients;
using DDCRM.Gateway.Api.Tests.Infrastructure;
using DDCRM.Shared.Authorization;

namespace DDCRM.Gateway.Api.Tests;

public sealed class GatewayApiIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProxyAccountApiAction_HappyPath_ReturnsWorkerPayload()
    {
        using var factory = new GatewayApiFactory();
        using var client = factory.CreateClient();

        var userId = Guid.NewGuid();
        var route = new RouteResolution(
            "rk.alpha",
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            new WorkerBinding("srv-1", "worker-1", "pod-1"));

        factory.RouteRegistryClient.NextRoute = route;
        factory.WorkerProxyClient.NextPayload = JsonSerializer.SerializeToElement(new
        {
            result = new
            {
                ok = true,
                source = "worker",
            },
        });

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(userId));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/account-api/rk.alpha/conversations.messages.send")
        {
            Content = JsonContent.Create(new
            {
                conversationId = "conv-100",
                text = "hello",
            }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("result", out var resultElement));
        Assert.True(resultElement.GetProperty("ok").GetBoolean());
        Assert.Equal("worker", resultElement.GetProperty("source").GetString());

        Assert.NotNull(factory.IamClient.LastCall);
        Assert.Equal(route.ProjectId, factory.IamClient.LastCall!.ProjectId);
        Assert.Equal(userId, factory.IamClient.LastCall.UserId);
        Assert.Equal(ProjectPermissions.ProjectWorkersOperate, factory.IamClient.LastCall.Permission);

        Assert.NotNull(factory.WorkerProxyClient.LastInvocation);
        Assert.Equal("conversations.messages.send", factory.WorkerProxyClient.LastInvocation!.Action);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProxyAccountApiAction_RevealAction_UsesProxyRevealPermission()
    {
        using var factory = new GatewayApiFactory();
        using var client = factory.CreateClient();

        var userId = Guid.NewGuid();
        factory.RouteRegistryClient.NextRoute = new RouteResolution(
            "rk.alpha",
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            new WorkerBinding("srv-1", "worker-1", "pod-1"));

        factory.WorkerProxyClient.NextPayload = JsonSerializer.SerializeToElement(new
        {
            result = new
            {
                proxyConfig = new
                {
                    host = "proxy.secure.internal",
                    port = 8081,
                },
            },
        });

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(userId));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/account-api/rk.alpha/ext.account.proxy-credentials.reveal")
        {
            Content = JsonContent.Create(new
            {
                reason = "support audit",
            }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.NotNull(factory.IamClient.LastCall);
        Assert.Equal(ProjectPermissions.ProjectAccountsProxyCredentialsReveal, factory.IamClient.LastCall!.Permission);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProxyAccountApiAction_ProxyApplyAction_UsesProxyUpdatePermission()
    {
        using var factory = new GatewayApiFactory();
        using var client = factory.CreateClient();

        factory.RouteRegistryClient.NextRoute = new RouteResolution(
            "rk.alpha",
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            new WorkerBinding("srv-1", "worker-1", "pod-1"));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(Guid.NewGuid()));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/account-api/rk.alpha/ext.account.proxy-credentials.apply")
        {
            Content = JsonContent.Create(new
            {
                payload = new
                {
                    accountId = Guid.NewGuid(),
                    proxyConfig = new
                    {
                        host = "proxy.internal",
                        port = 8081,
                        login = "proxy-user",
                        password = "proxy-pass",
                    },
                },
            }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.NotNull(factory.IamClient.LastCall);
        Assert.Equal(ProjectPermissions.ProjectAccountsProxyCredentialsUpdate, factory.IamClient.LastCall!.Permission);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProxyAccountApiAction_MarketplaceAuthApplyAction_UsesLifecyclePermission()
    {
        using var factory = new GatewayApiFactory();
        using var client = factory.CreateClient();

        factory.RouteRegistryClient.NextRoute = new RouteResolution(
            "rk.alpha",
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            new WorkerBinding("srv-1", "worker-1", "pod-1"));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(Guid.NewGuid()));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/account-api/rk.alpha/ext.account.marketplace-auth.apply")
        {
            Content = JsonContent.Create(new
            {
                payload = new
                {
                    accountId = Guid.NewGuid(),
                    marketplaceAuth = new
                    {
                        scheme = "golden_key",
                        credentials = new
                        {
                            golden_key = "funpay-golden-key",
                        },
                    },
                },
            }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.NotNull(factory.IamClient.LastCall);
        Assert.Equal(ProjectPermissions.ProjectAccountsLifecycleManage, factory.IamClient.LastCall!.Permission);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProxyAccountApiAction_RouteNotFound_ReturnsNotFound()
    {
        using var factory = new GatewayApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(Guid.NewGuid()));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/account-api/rk.missing/conversations.messages.send")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProxyAccountApiAction_WithInvalidAction_ReturnsBadRequest()
    {
        using var factory = new GatewayApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(Guid.NewGuid()));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/account-api/rk.alpha/BAD.ACTION")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProxyAccountApiAction_ExtensionWithoutCapability_ReturnsConflict()
    {
        using var factory = new GatewayApiFactory();
        using var client = factory.CreateClient();

        factory.RouteRegistryClient.NextRoute = new RouteResolution(
            "rk.alpha",
            Guid.NewGuid(),
            Guid.NewGuid(),
            5,
            new WorkerBinding("srv-1", "worker-1", "pod-1"));
        factory.WorkerProxyClient.NextCapabilitySupported = false;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(Guid.NewGuid()));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/account-api/rk.alpha/ext.market.sync")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ProxyAccountApiAction_WithoutBearerToken_ReturnsUnauthorized()
    {
        using var factory = new GatewayApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/account-api/rk.alpha/conversations.messages.send")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ProxyAccountApiAction_WhenPermissionDenied_ReturnsForbidden()
    {
        using var factory = new GatewayApiFactory();
        using var client = factory.CreateClient();

        factory.RouteRegistryClient.NextRoute = new RouteResolution(
            "rk.alpha",
            Guid.NewGuid(),
            Guid.NewGuid(),
            2,
            new WorkerBinding("srv-1", "worker-1", "pod-1"));
        factory.IamClient.NextAllowed = false;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(Guid.NewGuid()));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/account-api/rk.alpha/conversations.messages.send")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task ProxyAccountApiAction_RevealPermissionDenied_ReturnsForbiddenBeforeWorkerCall()
    {
        using var factory = new GatewayApiFactory();
        using var client = factory.CreateClient();

        factory.RouteRegistryClient.NextRoute = new RouteResolution(
            "rk.alpha",
            Guid.NewGuid(),
            Guid.NewGuid(),
            2,
            new WorkerBinding("srv-1", "worker-1", "pod-1"));
        factory.IamClient.NextAllowed = false;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(Guid.NewGuid()));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/account-api/rk.alpha/ext.account.proxy-credentials.reveal")
        {
            Content = JsonContent.Create(new
            {
                reason = "need reveal",
            }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        Assert.NotNull(factory.IamClient.LastCall);
        Assert.Equal(ProjectPermissions.ProjectAccountsProxyCredentialsReveal, factory.IamClient.LastCall!.Permission);
        Assert.Null(factory.WorkerProxyClient.LastInvocation);
    }

    [Fact]
    [Trait("Category", "Cors")]
    public async Task PreflightRequest_ReturnsConfiguredCorsHeaders()
    {
        using var factory = new GatewayApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/v1/account-api/rk.alpha/conversations.messages.send");
        request.Headers.Add("Origin", "https://app.ddcrm.local");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "Authorization,Content-Type,Idempotency-Key");

        var response = await client.SendAsync(request);

        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
        Assert.Contains("https://app.ddcrm.local", values);
    }
}
