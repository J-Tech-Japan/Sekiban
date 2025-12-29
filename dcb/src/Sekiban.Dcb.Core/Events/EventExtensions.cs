using Sekiban.Dcb.Common;

namespace Sekiban.Dcb.Events;

/// <summary>
///     Extension methods for Event
/// </summary>
public static class EventExtensions
{
    /// <summary>
    ///     Gets the timestamp of the event from its SortableUniqueId.
    ///     This represents when the event was created, not when it was persisted.
    /// </summary>
    /// <param name="evt">The event to get the timestamp from</param>
    /// <returns>The event timestamp as a UTC DateTimeOffset</returns>
    public static DateTimeOffset GetTimestamp(this Event evt)
    {
        var sortableId = new SortableUniqueId(evt.SortableUniqueIdValue);
        var dateTime = sortableId.GetDateTime();
        // GetDateTime() returns DateTime with DateTimeKind.Utc,
        // so we use the single-parameter constructor which preserves the Kind
        return new DateTimeOffset(dateTime);
    }
}
