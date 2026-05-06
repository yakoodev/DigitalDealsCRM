using System.Text.Json;

namespace DDCRM.Core.Api.Integrations;

internal static class RuntimeJson
{
    public static readonly JsonSerializerOptions Defaults = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };
}
