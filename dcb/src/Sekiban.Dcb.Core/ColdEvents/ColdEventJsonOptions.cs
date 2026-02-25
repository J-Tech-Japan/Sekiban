using System.Text.Json;
namespace Sekiban.Dcb.ColdEvents;

internal static class ColdEventJsonOptions
{
    internal static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
