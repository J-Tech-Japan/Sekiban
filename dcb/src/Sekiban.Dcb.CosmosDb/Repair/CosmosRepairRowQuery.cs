using Microsoft.Azure.Cosmos;
using Sekiban.Dcb.CosmosDb.Models;
namespace Sekiban.Dcb.CosmosDb.Repair;

/// <summary>
///     Finds the tag rows that index a given event, without letting the *string form* of the event id decide
///     correctness.
///     A pre-SEK-G2 row stored its event id as whatever <c>Guid.ToString()</c> produced, and a row written by
///     anything outside Sekiban could use any format at all. An equality predicate against one rendering
///     (<c>c.eventId = "0f8f…"</c>) silently drops the others — and a legacy row that is not returned looks
///     exactly like a missing row, so the repair would write a second row for a pair that is already indexed.
///     That is precisely the mistake this type exists to prevent.
///     So the split is deliberate:
///     <list type="bullet">
///         <item>
///             <description>
///                 The SQL predicate is a <b>superset prefilter</b> — it enumerates every rendering
///                 <see cref="Guid.ToString(string)" /> can produce, compared case-insensitively, so it cannot
///                 miss a row that <see cref="Guid.TryParse(string, out Guid)" /> would accept. It exists to
///                 avoid dragging the whole tag partition over the wire, not to decide anything.
///             </description>
///         </item>
///         <item>
///             <description>
///                 The <b>correctness gate</b> is <see cref="IsRowForEvent" />: every returned row's event id
///                 is parsed to a <see cref="Guid" /> and compared canonically. Format and casing cannot
///                 affect the outcome, and a row belonging to a different event is rejected here even if the
///                 prefilter let it through.
///             </description>
///         </item>
///     </list>
/// </summary>
internal static class CosmosRepairRowQuery
{
    /// <summary>
    ///     Every format <see cref="Guid.ToString(string)" /> can emit. Together with case-insensitive
    ///     comparison, these cover every string <see cref="Guid.TryParse(string, out Guid)" /> accepts.
    /// </summary>
    private static readonly string[] GuidFormats = { "D", "N", "B", "P", "X" };

    /// <summary>
    ///     Builds the partition-confined superset prefilter for the rows indexing <paramref name="eventId" />.
    /// </summary>
    public static QueryDefinition BuildCandidateQuery(string partitionKey, Guid eventId)
    {
        var predicates = GuidFormats
            .Select((_, index) => $"STRINGEQUALS(c.eventId, @eventId{index}, true)")
            .ToList();

        var query = new QueryDefinition(
                $"SELECT * FROM c WHERE c.pk = @pk AND ({string.Join(" OR ", predicates)})")
            .WithParameter("@pk", partitionKey);

        for (var index = 0; index < GuidFormats.Length; index++)
        {
            query = query.WithParameter($"@eventId{index}", eventId.ToString(GuidFormats[index]));
        }

        return query;
    }

    /// <summary>
    ///     The correctness gate: does this row index this event? Decided by canonical Guid comparison, never
    ///     by string form. A row whose event id will not parse indexes nothing we can identify, so it is not
    ///     ours.
    /// </summary>
    public static bool IsRowForEvent(CosmosTag row, Guid eventId) =>
        Guid.TryParse(row.EventId, out var rowEventId) && rowEventId == eventId;
}
