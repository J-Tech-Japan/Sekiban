using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.InMemory;

/// <summary>
///     Shared helper for writing serializable events to an in-memory event list.
///     Used by InternalInMemoryEventStore in both WithResult and WithoutResult packages
///     to avoid duplicating the WriteSerializableEventsAsync logic.
/// </summary>
public static class InMemorySerializableEventWriter
{
    /// <summary>
    ///     Writes serializable events to the provided event list and returns the result.
    ///     Caller must hold any necessary lock before invoking this method.
    /// </summary>
    /// <param name="events">The serializable events to write</param>
    /// <param name="addableStore">Adapter providing add and count operations on the underlying store</param>
    public static ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)> Write(
        IEnumerable<SerializableEvent> events,
        IAddableEventStore addableStore)
    {
        var list = events.ToList();

        foreach (var ev in list)
        {
            addableStore.AddSerializableEvent(ev);
        }

        var tagWrites = new List<TagWriteResult>();
        var uniqueTags = list.SelectMany(e => e.Tags).Distinct();
        foreach (var tagString in uniqueTags)
        {
            var tagEventCount = addableStore.CountEventsWithTag(tagString);
            tagWrites.Add(new TagWriteResult(tagString, tagEventCount, DateTimeOffset.UtcNow));
        }

        return ResultBox.FromValue<(IReadOnlyList<SerializableEvent>, IReadOnlyList<TagWriteResult>)>(
            (list.AsReadOnly(), tagWrites));
    }

    /// <summary>
    ///     Abstraction over the internal event store to allow shared write logic.
    /// </summary>
    public interface IAddableEventStore
    {
        void AddSerializableEvent(SerializableEvent ev);
        int CountEventsWithTag(string tagString);
    }
}
