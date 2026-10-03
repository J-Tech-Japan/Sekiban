using Newtonsoft.Json.Linq;
using Sekiban.Dcb.CosmosDb.Models;

namespace Sekiban.Dcb.CosmosDb;

/// <summary>Shared server filter and defensive materialization for tags-container queries.</summary>
internal static class CosmosTagQueryFilters
{
    public const string RowsOnly = "NOT IS_DEFINED(c.documentType)";

    public static string? EventId(JObject document) =>
        document.Property("documentType") == null &&
        !string.IsNullOrEmpty(document["eventId"]?.Value<string>())
            ? document["eventId"]!.Value<string>()
            : null;

    public static CosmosTag? ReadRow(JObject document) =>
        EventId(document) == null ? null : document.ToObject<CosmosTag>();

    public static IEnumerable<CosmosTag> ReadRows(IEnumerable<JObject> documents) =>
        documents.Select(ReadRow).OfType<CosmosTag>();
}
