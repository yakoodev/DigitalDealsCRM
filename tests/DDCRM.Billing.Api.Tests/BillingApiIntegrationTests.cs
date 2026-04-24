using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDCRM.Billing.Api.Tests.Infrastructure;

namespace DDCRM.Billing.Api.Tests;

public sealed class BillingApiIntegrationTests(BillingApiFactory factory) : IClassFixture<BillingApiFactory>
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task PaymentsCreate_WithSameIdempotencyKey_IsIdempotent()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var projectId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString("N");

        using var first = CreateMutatingRequest(
            "/internal/v1/payments/create",
            idempotencyKey,
            new
            {
                projectId,
                amount = 1200.50m,
                currency = "USD",
                planKey = "pro",
            });

        using var second = CreateMutatingRequest(
            "/internal/v1/payments/create",
            idempotencyKey,
            new
            {
                projectId,
                amount = 1200.50m,
                currency = "USD",
                planKey = "pro",
            });

        var firstResponse = await client.SendAsync(first);
        var secondResponse = await client.SendAsync(second);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        using var firstJson = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());

        var firstPaymentId = firstJson.RootElement.GetProperty("data").GetProperty("paymentId").GetGuid();
        var secondPaymentId = secondJson.RootElement.GetProperty("data").GetProperty("paymentId").GetGuid();
        Assert.Equal(firstPaymentId, secondPaymentId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PaymentsWebhook_DuplicateEvent_IsDeduplicated()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var projectId = Guid.NewGuid();
        var createIdempotencyKey = Guid.NewGuid().ToString("N");
        using var createRequest = CreateMutatingRequest(
            "/internal/v1/payments/create",
            createIdempotencyKey,
            new
            {
                projectId,
                amount = 999.99m,
                currency = "USD",
                planKey = "business",
            });

        var createResponse = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        using var createJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var paymentId = createJson.RootElement.GetProperty("data").GetProperty("paymentId").GetGuid();

        var eventId = $"evt-{Guid.NewGuid():N}";
        var payload = new
        {
            eventId,
            eventType = "payment.succeeded",
            paymentId,
            projectId,
            planKey = "business",
        };

        var firstWebhook = await client.PostAsJsonAsync("/internal/v1/payments/webhook", payload);
        var secondWebhook = await client.PostAsJsonAsync("/internal/v1/payments/webhook", payload);

        Assert.Equal(HttpStatusCode.OK, firstWebhook.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondWebhook.StatusCode);
        Assert.Equal(1, factory.CountWebhookEvents(eventId));

        var payment = factory.FindPayment(paymentId);
        Assert.NotNull(payment);
        Assert.Equal("succeeded", payment!.Status);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PaymentsRefund_WithSameIdempotencyKey_IsIdempotent()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var projectId = Guid.NewGuid();
        using var createRequest = CreateMutatingRequest(
            "/internal/v1/payments/create",
            Guid.NewGuid().ToString("N"),
            new
            {
                projectId,
                amount = 150m,
                currency = "USD",
            });

        var createResponse = await client.SendAsync(createRequest);
        using var createJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var paymentId = createJson.RootElement.GetProperty("data").GetProperty("paymentId").GetGuid();

        var refundIdempotency = Guid.NewGuid().ToString("N");
        using var firstRefund = CreateMutatingRequest(
            "/internal/v1/payments/refund",
            refundIdempotency,
            new
            {
                paymentId,
                amount = 100m,
                reason = "customer-request",
            });

        using var secondRefund = CreateMutatingRequest(
            "/internal/v1/payments/refund",
            refundIdempotency,
            new
            {
                paymentId,
                amount = 100m,
                reason = "customer-request",
            });

        var firstResponse = await client.SendAsync(firstRefund);
        var secondResponse = await client.SendAsync(secondRefund);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(1, factory.CountRefunds(paymentId));

        var payment = factory.FindPayment(paymentId);
        Assert.NotNull(payment);
        Assert.Equal("refunded", payment!.Status);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ManualActivate_ThenReconcile_TransitionsGraceToBlocked()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var projectId = Guid.NewGuid();
        using var activateRequest = CreateMutatingRequest(
            "/internal/v1/subscriptions/manual-activate",
            Guid.NewGuid().ToString("N"),
            new
            {
                projectId,
                planKey = "pro",
                actor = "ops-user",
                reason = "support-case",
            });

        var activateResponse = await client.SendAsync(activateRequest);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);

        var subscription = factory.FindSubscription(projectId);
        Assert.NotNull(subscription);
        Assert.Equal("active", subscription!.Status);

        factory.ForceSubscriptionGraceExpired(projectId);
        var reconcileResponse = await client.PostAsJsonAsync("/internal/v1/subscriptions/reconcile", new
        {
            projectId,
        });

        Assert.Equal(HttpStatusCode.Accepted, reconcileResponse.StatusCode);
        var updated = factory.FindSubscription(projectId);
        Assert.NotNull(updated);
        Assert.Equal("blocked", updated!.Status);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Webhook_WithUnsupportedEventType_ReturnsBadRequest()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "internal-token-a");

        var response = await client.PostAsJsonAsync("/internal/v1/payments/webhook", new
        {
            eventId = $"evt-{Guid.NewGuid():N}",
            eventType = "unknown.event",
            projectId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task PaymentsCreate_WithoutServiceToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();
        using var request = CreateMutatingRequest(
            "/internal/v1/payments/create",
            Guid.NewGuid().ToString("N"),
            new
            {
                projectId = Guid.NewGuid(),
                amount = 100,
            });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task PaymentsCreate_WithWorkerToken_ReturnsForbidden()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Service-Token", "worker-token-a");

        using var request = CreateMutatingRequest(
            "/internal/v1/payments/create",
            Guid.NewGuid().ToString("N"),
            new
            {
                projectId = Guid.NewGuid(),
                amount = 100,
            });

        var response = await client.SendAsync(request);
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
