using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Capabilities;

/// <summary>
///     The state accumulated while a native tagged stream is projected. This stays internal because it is shared
///     implementation plumbing for the actor and service paths, not another public projection surface.
/// </summary>
internal sealed record TaggedStreamProjectionState(
    ITagStatePayload State,
    int EventCount,
    string LastSortableUniqueId);

/// <summary>
///     Shares the consumer-side native tagged-stream invariants: ordinal delivery, duplicate handling, captured-head
///     guarding, deserialization, and one-event-at-a-time projection.
/// </summary>
internal static class TaggedStreamProjectionHelper
{
    /// <summary>
    ///     Projects the tag's native stream. Cancellation propagates, and so does a store that throws before returning
    ///     its stream task. Any other failure of the stream task (a store failure or one thrown by the projection
    ///     callback) is returned as <c>StreamFailure</c> after the caller's token has been observed, so each caller keeps
    ///     its own handling of a failed stream.
    /// </summary>
    internal static async Task<(TaggedStreamProjectionState? Projection, Exception? StreamFailure)> ProjectAsync(
        IStreamingTaggedSerializableEventStore streamStore,
        ITag tag,
        SortableUniqueId? since,
        SortableUniqueId? until,
        IEventTypes eventTypes,
        Func<ITagStatePayload, Event, ITagStatePayload> projector,
        ITagStatePayload initialState,
        string? initialPreviousId,
        string initialLastSortableUniqueId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = initialState;
        var eventCount = 0;
        var lastSortableUniqueId = initialLastSortableUniqueId;
        var previousId = initialPreviousId;
        var stream = streamStore.StreamSerializableEventsByTagAsync(
            tag,
            since,
            until,
            serializableEvent =>
            {
                // The provider can only stop between callback invocations. Observe before every consumer-side step so
                // a cancellation never deserializes or projects another event after it has reached this boundary.
                cancellationToken.ThrowIfCancellationRequested();
                if (!SekibanDcbCapabilityResolver.IsTaggedStreamOrderValid(
                        previousId,
                        serializableEvent.SortableUniqueIdValue,
                        out var duplicate))
                {
                    throw new InvalidOperationException(
                        $"Tagged stream for {tag.GetTag()} emitted out-of-order id " +
                        $"{serializableEvent.SortableUniqueIdValue} after {previousId}.");
                }

                if (duplicate)
                {
                    return ValueTask.CompletedTask;
                }

                if (until is { } capturedHead &&
                    string.Compare(
                        serializableEvent.SortableUniqueIdValue,
                        capturedHead.Value,
                        StringComparison.Ordinal) > 0)
                {
                    throw new InvalidOperationException(
                        $"Tagged stream for {tag.GetTag()} exceeded captured head {capturedHead.Value}.");
                }

                Event? domainEvent = null;
                Exception? deserializeFailure = null;
                try
                {
                    domainEvent = serializableEvent.ToEvent(eventTypes);
                }
                catch (Exception ex)
                {
                    deserializeFailure = ex;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (deserializeFailure is not null)
                {
                    throw new InvalidOperationException(
                        $"Failed to deserialize event for tag {tag.GetTag()}: {deserializeFailure.Message}",
                        deserializeFailure);
                }

                state = projector(state, domainEvent!);
                cancellationToken.ThrowIfCancellationRequested();
                eventCount++;
                lastSortableUniqueId = serializableEvent.SortableUniqueIdValue;
                previousId = serializableEvent.SortableUniqueIdValue;
                return ValueTask.CompletedTask;
            },
            cancellationToken);

        try
        {
            await stream;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return (null, ex);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return (new TaggedStreamProjectionState(state, eventCount, lastSortableUniqueId), null);
    }
}
