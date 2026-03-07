using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Storage;

/// <summary>
///     Streams SerializableEvent values directly to a caller-provided sink.
/// </summary>
public interface IStreamingSerializableEventStore
{
    Task<ResultBox<SerializableEventStreamReadResult>> StreamAllSerializableEventsAsync(
        SortableUniqueId? since,
        int? maxCount,
        Func<SerializableEvent, ValueTask> onEvent,
        CancellationToken cancellationToken = default);
}

public sealed record SerializableEventStreamReadResult(
    int EventsRead,
    string? LastSortableUniqueId);
