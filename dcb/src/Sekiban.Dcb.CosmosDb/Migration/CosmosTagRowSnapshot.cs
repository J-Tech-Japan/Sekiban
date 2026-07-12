using Sekiban.Dcb.CosmosDb.Models;
using System.Globalization;
using System.Text;
namespace Sekiban.Dcb.CosmosDb.Migration;

/// <summary>
///     Every field of a tag row that the migration cares about, frozen at the moment the plan was built.
///     A destructive run compares what it is about to delete against this — the whole row, not a semantic
///     summary of it. The only fields left out are the two Cosmos owns and rewrites on its own: <c>_etag</c>
///     and <c>_ts</c>. Everything else is content, and if any of it moved, the row is no longer the row the
///     operator reviewed, whatever it may still resemble.
/// </summary>
public record CosmosTagRowSnapshot
{
    /// <summary>Partition key.</summary>
    public string Pk { get; init; } = string.Empty;

    /// <summary>Service id.</summary>
    public string ServiceId { get; init; } = string.Empty;

    /// <summary>Document id.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Tag.</summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>Tag group.</summary>
    public string TagGroup { get; init; } = string.Empty;

    /// <summary>Event type.</summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>Sortable unique id.</summary>
    public string SortableUniqueId { get; init; } = string.Empty;

    /// <summary>Event id, as the row stores it.</summary>
    public string EventId { get; init; } = string.Empty;

    /// <summary>Creation timestamp, as the row stores it — legacy rows carry a wall-clock one.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Freezes a row.</summary>
    public static CosmosTagRowSnapshot From(CosmosTag row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new CosmosTagRowSnapshot
        {
            Pk = row.Pk,
            ServiceId = row.ServiceId,
            Id = row.Id,
            Tag = row.Tag,
            TagGroup = row.TagGroup,
            EventType = row.EventType,
            SortableUniqueId = row.SortableUniqueId,
            EventId = row.EventId,
            CreatedAt = row.CreatedAt
        };
    }

    /// <summary>
    ///     Whether a live row is byte-for-byte the row this snapshot froze, ignoring only <c>_etag</c> and
    ///     <c>_ts</c> — the fields Cosmos rewrites on every write and which therefore say nothing about
    ///     whether the row's content changed.
    ///     A legacy row that had its <c>createdAt</c> nudged, its tag group corrected, or anything else
    ///     touched is NOT a match. It might still be a perfectly good duplicate; that is beside the point.
    ///     It is not the one that was reviewed, so it is not the one that gets deleted.
    /// </summary>
    public bool Matches(CosmosTag live) =>
        live != null && Canonical(this) == Canonical(From(live));

    /// <summary>
    ///     A canonical, unambiguous encoding of the row's content, for hashing and comparison.
    ///     Every field is length-prefixed, so no arrangement of separators inside a tag, an id, or a type can
    ///     make two different rows encode the same way. Naive concatenation would let a tag containing the
    ///     separator shift every field after it — which, in a tool that deletes documents, is not a
    ///     theoretical concern.
    /// </summary>
    public static string Canonical(CosmosTagRowSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var builder = new StringBuilder();
        CosmosCanonicalEncoding.AppendField(builder, snapshot.Pk);
        CosmosCanonicalEncoding.AppendField(builder, snapshot.ServiceId);
        CosmosCanonicalEncoding.AppendField(builder, snapshot.Id);
        CosmosCanonicalEncoding.AppendField(builder, snapshot.Tag);
        CosmosCanonicalEncoding.AppendField(builder, snapshot.TagGroup);
        CosmosCanonicalEncoding.AppendField(builder, snapshot.EventType);
        CosmosCanonicalEncoding.AppendField(builder, snapshot.SortableUniqueId);
        CosmosCanonicalEncoding.AppendField(builder, snapshot.EventId);
        CosmosCanonicalEncoding.AppendField(
            builder,
            NormalizeToUtc(snapshot.CreatedAt).ToString("O", CultureInfo.InvariantCulture));

        return builder.ToString();
    }

    private static DateTime NormalizeToUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}

/// <summary>
///     Length-prefixed field encoding: <c>{byteLength}:{value};</c>.
///     Concatenating fields with a separator character is ambiguous the moment a value can contain that
///     character — two different plans can encode identically, and a fingerprint that cannot tell them apart
///     is a fingerprint that will authorize the wrong deletion. Prefixing each field with its length removes
///     the ambiguity entirely: there is nothing to escape and nothing to collide.
/// </summary>
internal static class CosmosCanonicalEncoding
{
    public static void AppendField(StringBuilder builder, string? value)
    {
        var text = value ?? string.Empty;
        builder
            .Append(text.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(text)
            .Append(';');
    }

    public static void AppendField(StringBuilder builder, bool value) =>
        AppendField(builder, value ? "true" : "false");
}
