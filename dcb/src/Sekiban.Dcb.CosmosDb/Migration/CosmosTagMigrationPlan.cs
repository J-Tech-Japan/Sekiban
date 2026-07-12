using Sekiban.Dcb.CosmosDb.Models;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Sekiban.Dcb.CosmosDb.Migration;

/// <summary>
///     One row the migration intends to delete, pinned to the exact version it was planned against.
/// </summary>
public record CosmosTagRowRef(string Id, string? ETag);

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

    /// <summary>
    ///     Document id of the row that will survive: the SEK-G2 deterministic id, always.
    /// </summary>
    public string SurvivorId { get; init; } = string.Empty;

    /// <summary>
    ///     Whether the survivor already exists. When false, the migration creates it before removing the
    ///     legacy rows — the pair is never left unindexed, not even momentarily.
    /// </summary>
    public bool SurvivorExists { get; init; }

    /// <summary>
    ///     The event's SortableUniqueId. Carried so the run can re-derive the canonical row rather than
    ///     promoting a legacy one: the survivor's content is what the write path would have written.
    /// </summary>
    public string SurvivorSortableUniqueId { get; init; } = string.Empty;

    /// <summary>The event's type, for the same reason.</summary>
    public string SurvivorEventType { get; init; } = string.Empty;

    /// <summary>Rows that will be deleted, each with the ETag it was planned against.</summary>
    public IReadOnlyList<CosmosTagRowRef> RowsToRemove { get; init; } = Array.Empty<CosmosTagRowRef>();
}

/// <summary>
///     The dry-run artifact: exactly what a destructive run would do, written down before anything is
///     touched, and required as the input to the run itself.
///     This is not paperwork. A destructive run takes a plan and nothing else, so an operator cannot delete
///     rows they have not first been shown. The <see cref="Fingerprint" /> covers the lineage and every
///     action, so an artifact that has been edited, truncated, or built for a different lineage is rejected
///     rather than half-applied.
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
    ///     Covers the lineage and every action. Recomputed when the plan is applied: an artifact that no
    ///     longer hashes to this has been altered, and is refused.
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
    ///     Hashes the lineage and the actions. Deterministic: the same plan always fingerprints the same, so
    ///     a re-planned identical world produces an identical artifact.
    /// </summary>
    public string ComputeFingerprint()
    {
        var builder = new StringBuilder();
        builder.Append(ServiceId).Append('|')
            .Append(EventsContainer).Append('|')
            .Append(TagsContainer).Append('|')
            .Append(FromSortableUniqueIdExclusive ?? string.Empty).Append('|')
            .Append(ToSortableUniqueIdInclusive ?? string.Empty).Append('\n');

        foreach (var action in Actions
            .OrderBy(action => action.PartitionKey, StringComparer.Ordinal)
            .ThenBy(action => action.EventId.ToString(), StringComparer.Ordinal))
        {
            builder.Append(action.PartitionKey).Append('|')
                .Append(action.EventId.ToString()).Append('|')
                .Append(action.SurvivorId).Append('|')
                .Append(action.SurvivorExists.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(action.SurvivorSortableUniqueId).Append('|')
                .Append(action.SurvivorEventType).Append('|');

            foreach (var row in action.RowsToRemove.OrderBy(row => row.Id, StringComparer.Ordinal))
            {
                builder.Append(row.Id).Append(':').Append(row.ETag ?? string.Empty).Append(',');
            }

            builder.Append('\n');
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
    Overflow
}
