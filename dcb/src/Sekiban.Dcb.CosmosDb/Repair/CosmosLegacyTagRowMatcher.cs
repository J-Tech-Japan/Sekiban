using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.CosmosDb.Tags;
namespace Sekiban.Dcb.CosmosDb.Repair;

/// <summary>
///     How an existing tag row relates to the (event, tag) key the repair derived.
/// </summary>
internal enum LegacyRowMatch
{
    /// <summary>The row indexes a different (event, tag). Not our key at all.</summary>
    NotOurKey,

    /// <summary>
    ///     The row indexes our key and its event-derived fields agree with the event. It differs from the
    ///     derived row only in the ways a pre-SEK-G2 row is expected to: a random id and a wall-clock
    ///     createdAt.
    /// </summary>
    LegacyPresent,

    /// <summary>
    ///     The row indexes our key but an immutable event-derived field disagrees with the event.
    /// </summary>
    Corrupt
}

/// <summary>
///     Recognizes tag rows written before the deterministic-id scheme (SEK-G2), which used
///     <c>Guid.NewGuid()</c> for the row id and <c>DateTime.UtcNow</c> for createdAt. Such a row indexes an
///     (event, tag) pair perfectly well — it just does not sit at the id the repair would derive — so
///     treating it as "missing" would write a second row for a pair that is already indexed.
///     This is deliberately NOT <see cref="CosmosTagIdentity.ContentEquals" />. That comparator is strict on
///     purpose: it decides whether a row at the *derived id* is the row we meant to write, so a differing id
///     or createdAt there really is corruption. For a legacy row those two fields are *expected* to differ,
///     and are reported as migration metadata rather than corruption. Every other field must still agree
///     with the event — a legacy id does not license drift in the data the row indexes.
/// </summary>
internal static class CosmosLegacyTagRowMatcher
{
    /// <summary>
    ///     Decides whether <paramref name="row" /> indexes the same (event, tag) as
    ///     <paramref name="derived" />, and if so whether it agrees with the event.
    ///     The semantic key is (serviceId, tag, eventId): serviceId and tag are compared ordinally, and the
    ///     event id is parsed to a <see cref="Guid" /> and compared canonically, so a row written with
    ///     different Guid formatting or casing still matches. Callers must confine the lookup to the tag's
    ///     partition (<c>pk = {serviceId}|{tag}</c>); combined with the event-id comparison, a different event
    ///     carrying the same tag can never match — its event id differs.
    /// </summary>
    public static LegacyRowMatch Classify(CosmosTag row, CosmosTag derived, out string? detail)
    {
        detail = null;

        if (!string.Equals(row.ServiceId, derived.ServiceId, StringComparison.Ordinal) ||
            !string.Equals(row.Tag, derived.Tag, StringComparison.Ordinal) ||
            !string.Equals(row.Pk, derived.Pk, StringComparison.Ordinal))
        {
            return LegacyRowMatch.NotOurKey;
        }

        if (!TryParseCanonical(row.EventId, out var rowEventId) ||
            !TryParseCanonical(derived.EventId, out var derivedEventId) ||
            rowEventId != derivedEventId)
        {
            return LegacyRowMatch.NotOurKey;
        }

        // The key matches, so this row indexes our pair. Everything the row copies from the event must
        // therefore agree with the event — a legacy row id excuses the id and the timestamp, nothing else.
        if (!string.Equals(row.SortableUniqueId, derived.SortableUniqueId, StringComparison.Ordinal))
        {
            detail =
                $"sortableUniqueId mismatch: row has '{row.SortableUniqueId}', event has '{derived.SortableUniqueId}'";
            return LegacyRowMatch.Corrupt;
        }

        if (!string.Equals(row.EventType, derived.EventType, StringComparison.Ordinal))
        {
            detail = $"eventType mismatch: row has '{row.EventType}', event has '{derived.EventType}'";
            return LegacyRowMatch.Corrupt;
        }

        if (!string.Equals(row.TagGroup, derived.TagGroup, StringComparison.Ordinal))
        {
            // tagGroup is derived from the tag string, so drift here means the row disagrees with its own
            // tag. Surfaced explicitly rather than folded into "legacy formatting".
            detail = $"tagGroup mismatch: row has '{row.TagGroup}', tag '{row.Tag}' derives '{derived.TagGroup}'";
            return LegacyRowMatch.Corrupt;
        }

        return LegacyRowMatch.LegacyPresent;
    }

    /// <summary>
    ///     Describes the expected, benign differences between a legacy row and the derived row, for the
    ///     report. Not corruption — migration metadata.
    /// </summary>
    public static string DescribeLegacyDifferences(CosmosTag row, CosmosTag derived)
    {
        var differences = new List<string>();

        if (!string.Equals(row.Id, derived.Id, StringComparison.Ordinal))
        {
            differences.Add($"row id '{row.Id}' (deterministic id would be '{derived.Id}')");
        }

        if (row.CreatedAt != derived.CreatedAt)
        {
            differences.Add($"createdAt '{row.CreatedAt:O}' (event-derived would be '{derived.CreatedAt:O}')");
        }

        return differences.Count == 0
            ? "no differences"
            : string.Join("; ", differences);
    }

    private static bool TryParseCanonical(string? value, out Guid parsed) => Guid.TryParse(value, out parsed);
}
