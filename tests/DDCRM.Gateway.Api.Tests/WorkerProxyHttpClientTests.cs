using System.Net;
using System.Text;
using System.Text.Json;
using DDCRM.Gateway.Api.Clients;
using Microsoft.Extensions.Options;

namespace DDCRM.Gateway.Api.Tests;

public sealed class WorkerProxyHttpClientTests
{
    [Fact]
    public async Task InvokeAsync_ConversationsList_MapsToV2GetWithQuery()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        var payload = ToPayload(new
        {
            limit = 25,
            cursor = "conv-100",
            onlyUnread = true,
        });

        _ = await client.InvokeAsync(
            CreateRoute(),
            "conversations.list",
            payload,
            idempotencyKey: "idem-1",
            cancellationToken: CancellationToken.None);

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.NotNull(handler.LastUri);
        Assert.Equal("/internal/v2/worker/conversations?limit=25&cursor=conv-100&onlyUnread=true", handler.LastUri!.PathAndQuery);
        Assert.Null(handler.LastBody);
    }

    [Fact]
    public async Task InvokeAsync_ProductsUpdate_MapsToV2PatchAndRemovesPathIdFromBody()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        var payload = ToPayload(new
        {
            productId = "product-777",
            expectedVersion = "12",
            changes = new
            {
                status = "active",
            },
        });

        _ = await client.InvokeAsync(
            CreateRoute(),
            "products.update",
            payload,
            idempotencyKey: "idem-2",
            cancellationToken: CancellationToken.None);

        Assert.Equal(HttpMethod.Patch, handler.LastMethod);
        Assert.NotNull(handler.LastUri);
        Assert.Equal("/internal/v2/worker/products/product-777", handler.LastUri!.PathAndQuery);
        Assert.NotNull(handler.LastBody);

        using var json = JsonDocument.Parse(handler.LastBody!);
        Assert.False(json.RootElement.TryGetProperty("productId", out _));
        Assert.Equal("12", json.RootElement.GetProperty("expectedVersion").GetString());
    }

    [Fact]
    public async Task InvokeAsync_ConversationMessageSend_UsesV2EndpointAndSanitizesPathId()
    {
        var handler = new CapturingHttpMessageHandler();
        var client = CreateClient(handler);

        var payload = ToPayload(new
        {
            conversationId = "conv-100",
            text = "hello",
        });

        _ = await client.InvokeAsync(
            CreateRoute(),
            "conversations.messages.send",
            payload,
            idempotencyKey: "idem-3",
            cancellationToken: CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.NotNull(handler.LastUri);
        Assert.Equal("/internal/v2/worker/conversations/conv-100/messages", handler.LastUri!.PathAndQuery);
        Assert.NotNull(handler.LastBody);

        using var json = JsonDocument.Parse(handler.LastBody!);
        Assert.False(json.RootElement.TryGetProperty("conversationId", out _));
        Assert.Equal("hello", json.RootElement.GetProperty("text").GetString());
    }

    private static WorkerProxyHttpClient CreateClient(HttpMessageHandler handler)
    {
        var options = Options.Create(new WorkerProxyClientOptions
        {
            Enabled = true,
            BaseUrlTemplate = "http://worker.local/{serverId}/{workerId}/{podId}",
            PathPrefix = "/internal/v2/worker",
            ServiceToken = "worker-token-a",
        });

        return new WorkerProxyHttpClient(new HttpClient(handler), options);
    }

    private static RouteResolution CreateRoute() =>
        new(
            "rk.alpha",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            1,
            new WorkerBinding("server-a", "worker-a", "pod-a"));

    private static Dictionary<string, JsonElement> ToPayload(object payload)
    {
        var element = JsonSerializer.SerializeToElement(payload);
        return element.EnumerateObject()
            .ToDictionary(x => x.Name, x => x.Value.Clone(), StringComparer.Ordinal);
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public HttpMethod? LastMethod { get; private set; }

        public Uri? LastUri { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastUri = request.RequestUri;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"result\":{\"ok\":true}}", Encoding.UTF8, "application/json"),
            };
        }
    }
}
