using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DDCRM.Core.Api.Workflows;

public static partial class WorkflowRuntimeTemplateResolver
{
    private static readonly Regex TokenRegex =
        new(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string RenderStringTemplate(
        string template,
        IReadOnlyDictionary<string, object?> variables)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        return TokenRegex.Replace(template, match =>
        {
            var path = match.Groups[1].Value.Trim();
            var resolved = ResolvePathValue(path, variables);
            return ConvertToTemplateString(resolved);
        });
    }

    public static object? ResolveJsonElementTemplates(
        JsonElement value,
        IReadOnlyDictionary<string, object?> variables)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Object => value.EnumerateObject()
                .ToDictionary(
                    x => x.Name,
                    x => ResolveJsonElementTemplates(x.Value, variables),
                    StringComparer.Ordinal),
            JsonValueKind.Array => value.EnumerateArray()
                .Select(x => ResolveJsonElementTemplates(x, variables))
                .ToList(),
            JsonValueKind.String => ResolveStringTemplateValue(value.GetString() ?? string.Empty, variables),
            JsonValueKind.Number when value.TryGetInt64(out var asLong) => asLong,
            JsonValueKind.Number when value.TryGetDecimal(out var asDecimal) => asDecimal,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    public static object? ResolveStringTemplateValue(
        string text,
        IReadOnlyDictionary<string, object?> variables)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var singleTokenMatch = TokenRegex.Match(text);
        var isSingleToken = singleTokenMatch.Success
            && singleTokenMatch.Index == 0
            && singleTokenMatch.Length == text.Length;

        if (isSingleToken)
        {
            var path = singleTokenMatch.Groups[1].Value.Trim();
            return ResolvePathValue(path, variables);
        }

        return RenderStringTemplate(text, variables);
    }

    public static object? ResolvePathValue(
        string path,
        IReadOnlyDictionary<string, object?> variables)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (variables.TryGetValue(path, out var direct))
        {
            return direct;
        }

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        if (!variables.TryGetValue(segments[0], out var current))
        {
            return null;
        }

        for (var i = 1; i < segments.Length; i += 1)
        {
            if (!TryReadNextSegment(current, segments[i], out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private static bool TryReadNextSegment(object? source, string segment, out object? value)
    {
        value = null;
        if (source is null)
        {
            return false;
        }

        if (source is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, segment, StringComparison.OrdinalIgnoreCase))
                    {
                        value = JsonElementToRuntimeValue(property.Value);
                        return true;
                    }
                }
            }

            return false;
        }

        if (source is IDictionary<string, object?> dict)
        {
            if (dict.TryGetValue(segment, out var found))
            {
                value = found;
                return true;
            }

            var pair = dict.FirstOrDefault(x => string.Equals(x.Key, segment, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(pair.Key))
            {
                value = pair.Value;
                return true;
            }

            return false;
        }

        if (source is IReadOnlyDictionary<string, object?> readOnlyDict)
        {
            if (readOnlyDict.TryGetValue(segment, out var found))
            {
                value = found;
                return true;
            }

            var pair = readOnlyDict.FirstOrDefault(x => string.Equals(x.Key, segment, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(pair.Key))
            {
                value = pair.Value;
                return true;
            }

            return false;
        }

        if (source is IDictionary<string, JsonElement> jsonDict)
        {
            if (jsonDict.TryGetValue(segment, out var jsonValue))
            {
                value = JsonElementToRuntimeValue(jsonValue);
                return true;
            }

            var pair = jsonDict.FirstOrDefault(x => string.Equals(x.Key, segment, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(pair.Key))
            {
                value = JsonElementToRuntimeValue(pair.Value);
                return true;
            }
        }

        var propertyInfo = source.GetType()
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .FirstOrDefault(x => string.Equals(x.Name, segment, StringComparison.OrdinalIgnoreCase));
        if (propertyInfo is null)
        {
            return false;
        }

        value = propertyInfo.GetValue(source);
        return true;
    }

    private static object? JsonElementToRuntimeValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var asLong) => asLong,
            JsonValueKind.Number when value.TryGetDecimal(out var asDecimal) => asDecimal,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object => value.EnumerateObject()
                .ToDictionary(
                    x => x.Name,
                    x => JsonElementToRuntimeValue(x.Value),
                    StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => value.EnumerateArray()
                .Select(JsonElementToRuntimeValue)
                .ToList(),
            _ => null,
        };
    }

    private static string ConvertToTemplateString(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string asString => asString,
            bool asBool => asBool ? "true" : "false",
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonElement element when element.ValueKind == JsonValueKind.Number => element.GetRawText(),
            JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False => element.GetBoolean() ? "true" : "false",
            JsonElement element when element.ValueKind is JsonValueKind.Object or JsonValueKind.Array => element.GetRawText(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => JsonSerializer.Serialize(value),
        };
    }
}
