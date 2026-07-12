using System.Text;
using System.Text.Json;
namespace Sekiban.Dcb.CosmosDb.Repair;

/// <summary>
///     Where a repair run stopped, encoded as an opaque token the caller hands back to resume.
///     It carries the last sortableUniqueId the run reached, and deliberately NOT a Cosmos continuation
///     token. A continuation token is bound to the exact query that produced it and expires, so it cannot be
///     handed across runs; the scan is ordered by sortableUniqueId, which is monotonic, so resuming at
///     "everything after the last one I saw" is both exact and durable. Continuation tokens are still used
///     for paging *within* a run, where the query is fixed.
///     Opaque on purpose: its shape is this service's business, not the caller's.
/// </summary>
internal sealed record CosmosTagRepairCheckpoint(string? LastSortableUniqueId)
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, SerializerOptions)));

    public static CosmosTagRepairCheckpoint? TryDecode(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            return JsonSerializer.Deserialize<CosmosTagRepairCheckpoint>(json, SerializerOptions);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            throw new ArgumentException(
                "The repair checkpoint is not a token produced by a previous run.",
                nameof(token),
                ex);
        }
    }
}
