using System.Text.Json;

namespace DDCRM.Core.Api.Workflows;

public static class WorkflowAuditRedactor
{
    private static readonly string[] SensitiveKeyFragments =
    [
        "password",
        "secret",
        "token",
        "cookie",
        "credential",
        "session",
        "authorization",
        "apikey",
        "api_key",
        "refresh",
    ];

    public static string SerializeRedacted(object? value)
    {
        var redacted = RedactValue(value, keyHint: null);
        return JsonSerializer.Serialize(redacted);
    }

    private static object? RedactValue(object? value, string? keyHint)
    {
        if (value is null)
        {
            return null;
        }

        if (IsSensitiveKey(keyHint))
        {
            return "***redacted***";
        }

        return value switch
        {
            JsonElement element => RedactJsonElement(element, keyHint),
            IDictionary<string, object?> dict => RedactDictionary(dict),
            IReadOnlyDictionary<string, object?> readOnlyDict => RedactReadOnlyDictionary(readOnlyDict),
            IEnumerable<object?> list => list.Select(x => RedactValue(x, keyHint: null)).ToList(),
            _ => value,
        };
    }

    private static object? RedactJsonElement(JsonElement value, string? keyHint)
    {
        if (IsSensitiveKey(keyHint))
        {
            return "***redacted***";
        }

        return value.ValueKind switch
        {
            JsonValueKind.Object => value.EnumerateObject()
                .ToDictionary(
                    x => x.Name,
                    x => RedactJsonElement(x.Value, x.Name),
                    StringComparer.Ordinal),
            JsonValueKind.Array => value.EnumerateArray()
                .Select(x => RedactJsonElement(x, keyHint: null))
                .ToList(),
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var asLong) => asLong,
            JsonValueKind.Number when value.TryGetDecimal(out var asDecimal) => asDecimal,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static Dictionary<string, object?> RedactDictionary(IDictionary<string, object?> source)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            result[pair.Key] = RedactValue(pair.Value, pair.Key);
        }

        return result;
    }

    private static Dictionary<string, object?> RedactReadOnlyDictionary(IReadOnlyDictionary<string, object?> source)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            result[pair.Key] = RedactValue(pair.Value, pair.Key);
        }

        return result;
    }

    private static bool IsSensitiveKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var normalized = key.Trim().ToLowerInvariant();
        return SensitiveKeyFragments.Any(fragment => normalized.Contains(fragment, StringComparison.Ordinal));
    }
}
