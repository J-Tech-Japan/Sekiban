using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.CosmosDb.Repair;
namespace Sekiban.Dcb.CosmosDb.Migration;

/// <summary>
///     Which row survives when several index the same (event, tag).
///     The rule has to be deterministic, because the plan an operator reads and the run that executes it are
///     two separate passes over the data: if the survivor could differ between them, the artifact would not
///     describe the run. It is also why re-planning an unchanged world produces a byte-identical artifact.
///     The rule:
///     <list type="number">
///         <item>
///             <description>
///                 The SEK-G2 deterministic-id row wins — the one whose document id is the event id. It is
///                 the row the write path produces today and the row every future write would produce, so
///                 keeping anything else would just guarantee another migration later.
///             </description>
///         </item>
///         <item>
///             <description>
///                 If no deterministic-id row exists, the migration CREATES it from the event, then removes
///                 the legacy rows. Deriving it from the event rather than promoting a legacy row means the
///                 survivor's content is the content the write path would have written — no legacy quirk
///                 survives the migration.
///             </description>
///         </item>
///     </list>
///     There is deliberately no "pick the oldest / the newest" tiebreak among legacy rows: they are never
///     promoted, only removed, so there is nothing to break a tie between. A legacy row is only ever removed
///     once a canonical row that agrees with the event is known to exist.
/// </summary>
internal static class CosmosTagSurvivorPolicy
{
    /// <summary>
    ///     Splits the rows of one (event, tag) into the survivor and the rows to remove.
    ///     Returns null when there is nothing to do — either no rows at all (the repair service's business,
    ///     not this one's) or already exactly the canonical row and nothing else.
    /// </summary>
    public static (CosmosTag Survivor, bool SurvivorExists, IReadOnlyList<CosmosTag> ToRemove)? Plan(
        IReadOnlyList<CosmosTag> rows,
        CosmosTag derived)
    {
        if (rows.Count == 0)
        {
            // Nothing indexes this key. Backfilling it is the non-destructive repair's job.
            return null;
        }

        var deterministic = rows.FirstOrDefault(row =>
            string.Equals(row.Id, derived.Id, StringComparison.Ordinal));

        var legacy = rows
            .Where(row => !string.Equals(row.Id, derived.Id, StringComparison.Ordinal))
            .OrderBy(row => row.Id, StringComparer.Ordinal) // stable order, so the artifact is reproducible
            .ToList();

        if (legacy.Count == 0)
        {
            // Only the canonical row is here. Already migrated.
            return null;
        }

        // The survivor is the canonical row: the existing one if it is there, otherwise the one derived from
        // the event — which is exactly what the write path would produce.
        return (deterministic ?? derived, deterministic != null, legacy);
    }

    /// <summary>
    ///     Whether every row about to be removed is a legacy row that genuinely indexes this event and agrees
    ///     with it. A row that disagrees is not a duplicate — it is corruption, and this service does not get
    ///     to decide what to do about corruption.
    /// </summary>
    public static bool AllRowsAreSafeToRemove(
        IReadOnlyList<CosmosTag> toRemove,
        CosmosTag derived,
        out string detail)
    {
        foreach (var row in toRemove)
        {
            var match = CosmosLegacyTagRowMatcher.Classify(row, derived, out var mismatch);

            if (match != LegacyRowMatch.LegacyPresent)
            {
                detail = mismatch ??
                    $"row '{row.Id}' does not index this (event, tag) as the event describes it";
                return false;
            }
        }

        detail = string.Empty;
        return true;
    }
}
