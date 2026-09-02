using ResultBoxes;
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
    internal static async Task<ResultBox<TaggedStreamProjectionState>> ProjectAsync(
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
        var state = initialState;
        var eventCount = 0;
        var lastSortableUniqueId = initialLastSortableUniqueId;
        var previousId = initialPreviousId;
        var streamResult = await streamStore.StreamSerializableEventsByTagAsync(
            tag,
            since,
            until,
            serializableEvent =>
            {
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

                var eventResult = serializableEvent.ToEvent(eventTypes);
                if (!eventResult.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"Failed to deserialize event for tag {tag.GetTag()}: {eventResult.GetException().Message}",
                        eventResult.GetException());
                }

                state = projector(state, eventResult.GetValue());
                eventCount++;
                lastSortableUniqueId = serializableEvent.SortableUniqueIdValue;
                previousId = serializableEvent.SortableUniqueIdValue;
                return ValueTask.CompletedTask;
            },
            cancellationToken);

        if (!streamResult.IsSuccess)
        {
            return ResultBox.Error<TaggedStreamProjectionState>(streamResult.GetException());
        }

        return ResultBox.FromValue(new TaggedStreamProjectionState(state, eventCount, lastSortableUniqueId));
    }
}
