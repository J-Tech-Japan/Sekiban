using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb.Models;
namespace Sekiban.Dcb.CosmosDb.Tags;

/// <summary>
///     Deterministic derivation rule for Cosmos tag rows.
///     A tag row is a projection of an (event, tag) pair: given the event document — which carries the
///     complete <c>tags</c> array — and one tag from it, every field of the tag row is fully derivable.
///     Re-deriving a row for the same (event, tag) pair always yields the same document id, partition key,
///     and content, which is what makes the tag-write stage safely re-executable and the tags container a
///     rebuildable index over the events container.
///     Derivation rule (v1):
///     <list type="bullet">
///         <item>
///             <description><c>pk</c> = <c>{serviceId}|{tag}</c></description>
///         </item>
///         <item>
///             <description>
///                 <c>id</c> = the event id. A tag row is unique per (service, tag, event), and the
///                 partition key already pins service and tag, so the event id alone is a unique — and
///                 trivially re-derivable — document id inside that partition.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <c>tagGroup</c> = the segment before the first <c>:</c> of the tag, or the whole tag
///                 when it has no <c>:</c>.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <c>createdAt</c> = the timestamp encoded in the event's SortableUniqueId (UTC). It is
///                 derived from the event rather than read from the wall clock so that re-execution
///                 produces byte-identical content.
///             </description>
///         </item>
///     </list>
/// </summary>
public static class CosmosTagIdentity
{
    /// <summary>
    ///     Derives the partition key of the tag row for a (service, tag) pair.
    /// </summary>
    public static string DerivePartitionKey(string serviceId, string tag) => $"{serviceId}|{tag}";

    /// <summary>
    ///     Derives the document id of the tag row for an (event, tag) pair.
    /// </summary>
    public static string DeriveId(Guid eventId) => eventId.ToString();

    /// <summary>
    ///     Derives the tag group of a tag string.
    /// </summary>
    public static string DeriveTagGroup(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return tag.Contains(':', StringComparison.Ordinal) ? tag.Split(':')[0] : tag;
    }

    /// <summary>
    ///     Derives the creation timestamp of the tag row from the event's SortableUniqueId.
    /// </summary>
    public static DateTime DeriveCreatedAt(string sortableUniqueId)
    {
        ArgumentNullException.ThrowIfNull(sortableUniqueId);
        return DateTime.SpecifyKind(new SortableUniqueId(sortableUniqueId).GetDateTime(), DateTimeKind.Utc);
    }

    /// <summary>
    ///     Derives the complete tag row for an (event, tag) pair.
    /// </summary>
    public static CosmosTag DeriveRow(
        string serviceId,
        string tag,
        Guid eventId,
        string sortableUniqueId,
        string eventType)
    {
        ArgumentNullException.ThrowIfNull(serviceId);
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(sortableUniqueId);
        ArgumentNullException.ThrowIfNull(eventType);

        return new CosmosTag
        {
            Pk = DerivePartitionKey(serviceId, tag),
            ServiceId = serviceId,
            Id = DeriveId(eventId),
            Tag = tag,
            TagGroup = DeriveTagGroup(tag),
            EventType = eventType,
            SortableUniqueId = sortableUniqueId,
            EventId = eventId.ToString(),
            CreatedAt = DeriveCreatedAt(sortableUniqueId)
        };
    }

    /// <summary>
    ///     Compares two tag rows by their derived content, ignoring Cosmos-managed fields such as
    ///     <c>_etag</c> and <c>_ts</c>. Timestamps are normalized to UTC before comparison so that a
    ///     serializer round-trip which changes only <see cref="DateTimeKind" /> is not reported as a mismatch.
    ///     Used to decide whether an already-existing tag row is the row we intended to write (idempotent
    ///     re-execution) or a different row occupying the same identity (index corruption).
    /// </summary>
    public static bool ContentEquals(CosmosTag left, CosmosTag right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return string.Equals(left.Pk, right.Pk, StringComparison.Ordinal) &&
            string.Equals(left.ServiceId, right.ServiceId, StringComparison.Ordinal) &&
            string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
            string.Equals(left.Tag, right.Tag, StringComparison.Ordinal) &&
            string.Equals(left.TagGroup, right.TagGroup, StringComparison.Ordinal) &&
            string.Equals(left.EventType, right.EventType, StringComparison.Ordinal) &&
            string.Equals(left.SortableUniqueId, right.SortableUniqueId, StringComparison.Ordinal) &&
            string.Equals(left.EventId, right.EventId, StringComparison.Ordinal) &&
            NormalizeToUtc(left.CreatedAt) == NormalizeToUtc(right.CreatedAt);
    }

    /// <summary>
    ///     Describes the content of a tag row for diagnostics. Used in corruption error messages.
    /// </summary>
    public static string Describe(CosmosTag row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return $"pk={row.Pk}, id={row.Id}, tag={row.Tag}, tagGroup={row.TagGroup}, eventId={row.EventId}, " +
            $"eventType={row.EventType}, sortableUniqueId={row.SortableUniqueId}, " +
            $"createdAt={NormalizeToUtc(row.CreatedAt):O}";
    }

    private static DateTime NormalizeToUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
