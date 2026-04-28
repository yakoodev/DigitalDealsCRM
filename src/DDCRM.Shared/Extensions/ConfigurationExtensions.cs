using Microsoft.Extensions.Configuration;

namespace DDCRM.Shared.Extensions;

public static class ConfigurationExtensions
{
    public static string[] GetCommaSeparatedValues(this IConfiguration configuration, string key)
    {
        var raw = configuration[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
}
