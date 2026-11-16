using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Storage;

/// <summary>
///     Shares the callback dispatch mechanics for in-memory stores that first capture an immutable event snapshot.
///     Database providers intentionally do not use this helper: their native readers retain provider-side bound pushdown
///     and cancellation behavior.
/// </summary>
internal static class TaggedSerializableEventStreamHelper
{
    internal static async Task<SerializableEventStreamReadResult> StreamSnapshotByTagAsync<TEvent>(
        object snapshotLock,
        IEnumerable<TEvent> events,
        Func<TEvent, IEnumerable<string>> getTags,
        Func<TEvent, string> getSortableUniqueId,
        Func<TEvent, SerializableEvent> serialize,
        ITag tag,
        SortableUniqueId? since,
        SortableUniqueId? until,
        Func<SerializableEvent, ValueTask> onEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tagString = tag.GetTag();
        List<SerializableEvent> snapshot;
        lock (snapshotLock)
        {
            snapshot = events
                .Where(@event => getTags(@event).Contains(tagString))
                .Where(@event => since is null || string.Compare(
                    getSortableUniqueId(@event),
                    since.Value,
                    StringComparison.Ordinal) > 0)
                .Where(@event => until is null || string.Compare(
                    getSortableUniqueId(@event),
                    until.Value,
                    StringComparison.Ordinal) <= 0)
                .OrderBy(getSortableUniqueId, StringComparer.Ordinal)
                .Select(serialize)
                .ToList();
        }

        var count = 0;
        string? lastSortableUniqueId = null;
        foreach (var serializableEvent in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await onEvent(serializableEvent);
            cancellationToken.ThrowIfCancellationRequested();
            count++;
            lastSortableUniqueId = serializableEvent.SortableUniqueIdValue;
        }

        return new SerializableEventStreamReadResult(count, lastSortableUniqueId);
    }
}
