using System.Text.Json;
using DDCRM.Core.Api.Billing;

namespace DDCRM.Core.Api.Tests.Infrastructure;

public sealed class FakeBillingClient : IBillingClient
{
    private int _paymentSequence = 1;

    public List<CreatePaymentCall> CreatePaymentCalls { get; } = [];

    public List<ManualActivateCall> ManualActivateCalls { get; } = [];

    public string ManualActivateStatus { get; set; } = "completed";

    public Task<IDictionary<string, object?>> CreatePaymentAsync(
        Guid projectId,
        IDictionary<string, JsonElement>? payload,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var payloadData = payload is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : payload.ToDictionary(x => x.Key, x => ConvertJsonElement(x.Value), StringComparer.Ordinal);

        CreatePaymentCalls.Add(new CreatePaymentCall(projectId, idempotencyKey, payloadData));

        var response = new Dictionary<string, object?>(payloadData, StringComparer.Ordinal)
        {
            ["paymentId"] = $"pay-test-{_paymentSequence++:D4}",
            ["projectId"] = projectId,
            ["idempotencyKey"] = idempotencyKey,
        };

        return Task.FromResult<IDictionary<string, object?>>(response);
    }

    public Task<string> ManualActivateSubscriptionAsync(
        Guid projectId,
        IDictionary<string, JsonElement>? payload,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var payloadData = payload is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : payload.ToDictionary(x => x.Key, x => ConvertJsonElement(x.Value), StringComparer.Ordinal);

        ManualActivateCalls.Add(new ManualActivateCall(projectId, idempotencyKey, payloadData));

        return Task.FromResult(ManualActivateStatus);
    }

    public void Reset()
    {
        _paymentSequence = 1;
        ManualActivateStatus = "completed";
        CreatePaymentCalls.Clear();
        ManualActivateCalls.Clear();
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var intValue) => intValue,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(x => x.Name, x => ConvertJsonElement(x.Value), StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToArray(),
            _ => element.GetRawText(),
        };
    }
}

public sealed record CreatePaymentCall(Guid ProjectId, string IdempotencyKey, IReadOnlyDictionary<string, object?> Payload);

public sealed record ManualActivateCall(Guid ProjectId, string IdempotencyKey, IReadOnlyDictionary<string, object?> Payload);
