using DcbLib.Tags;
using ResultBoxes;

namespace DcbLib.Events;

/// <summary>
/// Represents either an event with tags or no event.
/// Used in command handlers to indicate optional event emission.
/// </summary>
public record EventOrNone(EventPayloadWithTags? EventWithTags, bool HasEvent)
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
        HasEvent && EventWithTags is not null 
            ? EventWithTags 
            : throw new ResultsInvalidOperationException("No value");

    /// <summary>
    /// Implicit conversion from UnitValue to represent no event
    /// </summary>
    public static implicit operator EventOrNone(UnitValue value) => Empty;
}