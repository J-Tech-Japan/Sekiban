using Newtonsoft.Json.Linq;
using Sekiban.Dcb.CosmosDb.Models;

namespace Sekiban.Dcb.CosmosDb;

/// <summary>Shared server filter and defensive materialization for tags-container queries.</summary>
internal static class CosmosTagQueryFilters
{
    public const string RowsOnly = "NOT IS_DEFINED(c.documentType)";

    // Constant query texts: values are always passed as parameters, never formatted into the SQL.
    public const string AllRowsByService = "SELECT * FROM c WHERE c.serviceId = @serviceId AND " + RowsOnly;

    public const string AllRowsByServiceAndGroup = AllRowsByService + " AND c.tagGroup = @tagGroup";

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
