using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Sekiban.Dcb.CosmosDb.Migration;

/// <summary>
///     One row the migration intends to delete, pinned to the exact version AND the exact content it was
///     planned against.
///     The ETag guards the version; the snapshot guards the content. Both are needed: Cosmos will happily let
///     you delete a row whose ETag you re-read, so a version check alone degenerates into "delete whatever is
///     there now".
/// </summary>
public record CosmosTagRowRef
{
    /// <summary>Document id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The version the plan pinned. A delete names it, so Cosmos refuses if the row has moved.</summary>
    public string? ETag { get; init; }

    /// <summary>The content the plan pinned. A delete happens only against exactly this.</summary>
    public CosmosTagRowSnapshot Snapshot { get; init; } = new();
}

/// <summary>
///     What the migration intends to do to one (event, tag) key: keep one canonical row, remove the rest.
/// </summary>
public record CosmosTagMigrationAction
{
    /// <summary>Event whose tag rows are being reduced.</summary>
    public Guid EventId { get; init; }

    /// <summary>Tag whose partition holds them.</summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>Partition key of every row below.</summary>
    public string PartitionKey { get; init; } = string.Empty;

    /// <summary>Document id of the row that will survive: the SEK-G2 deterministic id, always.</summary>
    public string SurvivorId { get; init; } = string.Empty;

    /// <summary>
    ///     Whether the survivor already existed when the plan was built. Either way the run proves the
    ///     canonical row is present and correct immediately before it deletes anything.
    /// </summary>
    public bool SurvivorExists { get; init; }

    /// <summary>
    ///     The exact content the canonical row must have: derived from the event, so it is what the write
    ///     path would have written. The run creates it from this when absent, and refuses to delete a single
    ///     legacy row unless the live survivor matches it exactly.
    /// </summary>
    public CosmosTagRowSnapshot SurvivorExpected { get; init; } = new();

    /// <summary>The survivor's version when the plan was built, when it existed. Informational.</summary>
    public string? SurvivorETag { get; init; }

    /// <summary>Rows that will be deleted, each pinned to its version and its content.</summary>
    public IReadOnlyList<CosmosTagRowRef> RowsToRemove { get; init; } = Array.Empty<CosmosTagRowRef>();
}

/// <summary>
///     The dry-run artifact: exactly what a destructive run would do, written down before anything is
///     touched, and required as the input to the run itself.
///     This is not paperwork. A destructive run takes a plan and nothing else, so an operator cannot delete
///     rows they have not first been shown. The <see cref="Fingerprint" /> covers the lineage and every
///     mutation-relevant field of every action — under a length-prefixed encoding, so no arrangement of
///     characters inside a tag or an id can make two different plans hash the same. An artifact that has been
///     edited, truncated, or built for a different lineage is rejected rather than half-applied.
/// </summary>
public record CosmosTagMigrationPlan
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true
    };

    /// <summary>Service id this plan was built for. A run against another lineage is refused.</summary>
    public string ServiceId { get; init; } = string.Empty;

    /// <summary>Events container the plan was built from.</summary>
    public string EventsContainer { get; init; } = string.Empty;

    /// <summary>Tags container the plan would mutate.</summary>
    public string TagsContainer { get; init; } = string.Empty;

    /// <summary>Lower bound of the scanned range, exclusive.</summary>
    public string? FromSortableUniqueIdExclusive { get; init; }

    /// <summary>Upper bound of the scanned range, inclusive.</summary>
    public string? ToSortableUniqueIdInclusive { get; init; }

    /// <summary>Events examined while building this plan.</summary>
    public int EventsScanned { get; init; }

    /// <summary>Keys examined while building this plan.</summary>
    public int KeysScanned { get; init; }

    /// <summary>Keys the plan would reduce.</summary>
    public IReadOnlyList<CosmosTagMigrationAction> Actions { get; init; } =
        Array.Empty<CosmosTagMigrationAction>();

    /// <summary>
    ///     Keys the plan deliberately leaves alone, and why: a row that disagrees with its event, or more
    ///     rows than the per-key cap allowed it to examine. The migration never deletes what it does not
    ///     understand.
    /// </summary>
    public IReadOnlyList<CosmosTagMigrationSkip> Skipped { get; init; } =
        Array.Empty<CosmosTagMigrationSkip>();

    /// <summary>True when the range was not fully scanned; resume from <see cref="Checkpoint" />.</summary>
    public bool HasMore { get; init; }

    /// <summary>Opaque resume token for the next bounded plan.</summary>
    public string? Checkpoint { get; init; }

    /// <summary>
    ///     Covers the lineage and every mutation-relevant field. Recomputed when the plan is applied: an
    ///     artifact that no longer hashes to this has been altered, and is refused.
    /// </summary>
    public string Fingerprint { get; init; } = string.Empty;

    /// <summary>Rows this plan would delete, in total.</summary>
    public int RowsToRemoveCount => Actions.Sum(action => action.RowsToRemove.Count);

    /// <summary>Serializes the artifact for an operator to read, keep, and hand back to the run.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>Reads an artifact back.</summary>
    public static CosmosTagMigrationPlan FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize<CosmosTagMigrationPlan>(json, SerializerOptions)
            ?? throw new ArgumentException("The migration plan is empty.", nameof(json));
    }

    /// <summary>
    ///     Hashes the lineage and every field that decides what gets deleted — the tag, the partition key,
    ///     the event, the survivor's expected content, and each removable row's id, version and full content.
    ///     Every field is length-prefixed, so no value containing a separator can shift the ones after it and
    ///     make two different plans collide. Deterministic: the same plan always fingerprints the same, so
    ///     re-planning an unchanged world produces an identical artifact.
    /// </summary>
    public string ComputeFingerprint()
    {
        var builder = new StringBuilder();

        CosmosCanonicalEncoding.AppendField(builder, ServiceId);
        CosmosCanonicalEncoding.AppendField(builder, EventsContainer);
        CosmosCanonicalEncoding.AppendField(builder, TagsContainer);
        CosmosCanonicalEncoding.AppendField(builder, FromSortableUniqueIdExclusive);
        CosmosCanonicalEncoding.AppendField(builder, ToSortableUniqueIdInclusive);
        CosmosCanonicalEncoding.AppendField(builder, Actions.Count.ToString(null as IFormatProvider));

        foreach (var action in Actions
            .OrderBy(action => action.PartitionKey, StringComparer.Ordinal)
            .ThenBy(action => action.EventId.ToString(), StringComparer.Ordinal))
        {
            CosmosCanonicalEncoding.AppendField(builder, action.PartitionKey);
            CosmosCanonicalEncoding.AppendField(builder, action.Tag);
            CosmosCanonicalEncoding.AppendField(builder, action.EventId.ToString());
            CosmosCanonicalEncoding.AppendField(builder, action.SurvivorId);
            CosmosCanonicalEncoding.AppendField(builder, action.SurvivorExists);
            CosmosCanonicalEncoding.AppendField(builder, action.SurvivorETag);
            CosmosCanonicalEncoding.AppendField(builder, CosmosTagRowSnapshot.Canonical(action.SurvivorExpected));
            CosmosCanonicalEncoding.AppendField(
                builder,
                action.RowsToRemove.Count.ToString(null as IFormatProvider));

            foreach (var row in action.RowsToRemove.OrderBy(row => row.Id, StringComparer.Ordinal))
            {
                CosmosCanonicalEncoding.AppendField(builder, row.Id);
                CosmosCanonicalEncoding.AppendField(builder, row.ETag);
                CosmosCanonicalEncoding.AppendField(builder, CosmosTagRowSnapshot.Canonical(row.Snapshot));
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}

/// <summary>Why the plan is leaving a key alone.</summary>
public record CosmosTagMigrationSkip(Guid EventId, string Tag, CosmosTagMigrationSkipReason Reason, string Detail);

/// <summary>Bounded set of reasons a key is left alone.</summary>
public enum CosmosTagMigrationSkipReason
{
    /// <summary>A row occupying the key disagrees with its event. Never deleted, never rewritten.</summary>
    Corrupt,

    /// <summary>More rows than the per-key cap allowed the scan to examine.</summary>
    Overflow,

    /// <summary>
    ///     The key needs more operations than one Cosmos transaction can carry. Refused at plan time:
    ///     splitting it across transactions would put a gap back between the survivor's guarantee and the
    ///     deletes, which is precisely what the atomic reduce exists to remove.
    /// </summary>
    BatchLimit
}
