using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Storage;

/// <summary>
///     Optional capability for reading one tag's serialized events directly into a caller-provided sink.
///     The existing <see cref="IEventStore.ReadSerializableEventsByTagAsync" /> list contract intentionally remains
///     unchanged for compatibility.
/// </summary>
public interface IStreamingTaggedSerializableEventStore
{
    /// <summary>
    ///     Streams events whose sortable unique id is greater than <paramref name="since" /> and less than or equal to
    ///     <paramref name="until" />. Providers emit strictly increasing ordinal ids, return non-cancellation failures
    ///     as <see cref="ResultBox{T}.Error" />, and propagate cancellation.
    /// </summary>
    Task<ResultBox<SerializableEventStreamReadResult>> StreamSerializableEventsByTagAsync(
        ITag tag,
        SortableUniqueId? since,
        SortableUniqueId? until,
        Func<SerializableEvent, ValueTask> onEvent,
        CancellationToken cancellationToken = default);
}
