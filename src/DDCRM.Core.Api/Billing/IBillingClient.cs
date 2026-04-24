using System.Text.Json;

namespace DDCRM.Core.Api.Billing;

public interface IBillingClient
{
    Task<IDictionary<string, object?>> CreatePaymentAsync(
        Guid projectId,
        IDictionary<string, JsonElement>? payload,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<string> ManualActivateSubscriptionAsync(
        Guid projectId,
        IDictionary<string, JsonElement>? payload,
        string idempotencyKey,
        CancellationToken cancellationToken);
}
