using Sekiban.Dcb.Tags;
using ResultBoxes;

namespace Sekiban.Dcb.Events;

/// <summary>
/// Represents either an event with tags or no event.
/// Used in command handlers to indicate optional event emission.
/// </summary>
public record EventOrNone(EventPayloadWithTags? EventPayloadWithTags, bool HasEvent)
{
    /// <summary>
    /// Represents no event
    /// </summary>
    public static EventOrNone Empty => new(default, false);
    
    /// <summary>
    /// ResultBox containing no event
    /// </summary>
    public static ResultBox<EventOrNone> None => Empty;

    /// <summary>
    /// Creates an EventOrNone from an EventPayloadWithTags
    /// </summary>
    public static EventOrNone FromValue(EventPayloadWithTags eventWithTags) => 
        new(eventWithTags, true);

    /// <summary>
    /// Creates an EventOrNone from an event payload and tags
    /// </summary>
    public static EventOrNone FromValue(IEventPayload eventPayload, params ITag[] tags) => 
        new(new EventPayloadWithTags(eventPayload, tags.ToList()), true);

    /// <summary>
    /// Creates an EventOrNone from an event payload and a list of tags
    /// </summary>
    public static EventOrNone FromValue(IEventPayload eventPayload, List<ITag> tags) => 
        new(new EventPayloadWithTags(eventPayload, tags), true);

    /// <summary>
    /// Creates a ResultBox containing an event with tags
    /// </summary>
    public static ResultBox<EventOrNone> Event(EventPayloadWithTags eventWithTags) => 
        ResultBox.FromValue(FromValue(eventWithTags));
    public static ResultBox<EventOrNone> EventWithTags(
        IEventPayload eventPayload,
        params IEnumerable<ITag> tags) => 
        ResultBox.FromValue(FromValue(new EventPayloadWithTags(eventPayload, tags.ToList())));

    /// <summary>
    /// Creates a ResultBox containing an event with tags
    /// </summary>
    public static ResultBox<EventOrNone> Event(IEventPayload eventPayload, params ITag[] tags) => 
        ResultBox.FromValue(FromValue(eventPayload, tags));

    /// <summary>
    /// Creates a ResultBox containing an event with tags
    /// </summary>
    public static ResultBox<EventOrNone> Event(IEventPayload eventPayload, List<ITag> tags) => 
        ResultBox.FromValue(FromValue(eventPayload, tags));

    /// <summary>
    /// Gets the EventPayloadWithTags value
    /// </summary>
    /// <exception cref="ResultsInvalidOperationException">Thrown when there is no event</exception>
    public EventPayloadWithTags GetValue() =>
        HasEvent && EventPayloadWithTags is not null 
            ? EventPayloadWithTags 
            : throw new ResultsInvalidOperationException("No value");

    /// <summary>
    /// Implicit conversion from UnitValue to represent no event
    /// </summary>
    public static implicit operator EventOrNone(UnitValue value) => Empty;
}