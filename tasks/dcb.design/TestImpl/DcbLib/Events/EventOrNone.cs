using DcbLib.Tags;
using ResultBoxes;

namespace DcbLib.Events;

/// <summary>
/// Represents either an event with tags or no event.
/// Used in command handlers to indicate optional event emission.
/// </summary>
public record EventOrNone(IEventPayload? EventPayload, IReadOnlyList<ITag>? Tags, bool HasEvent)
{
    /// <summary>
    /// Represents no event
    /// </summary>
    public static EventOrNone Empty => new(default, default, false);
    
    /// <summary>
    /// ResultBox containing no event
    /// </summary>
    public static ResultBox<EventOrNone> None => Empty;

    /// <summary>
    /// Creates an EventOrNone from an event payload and tags
    /// </summary>
    public static EventOrNone FromValue(IEventPayload eventPayload, params ITag[] tags) => 
        new(eventPayload, tags.ToList(), true);

    /// <summary>
    /// Creates an EventOrNone from an event payload and a list of tags
    /// </summary>
    public static EventOrNone FromValue(IEventPayload eventPayload, IReadOnlyList<ITag> tags) => 
        new(eventPayload, tags, true);

    /// <summary>
    /// Creates a ResultBox containing an event with tags
    /// </summary>
    public static ResultBox<EventOrNone> Event(IEventPayload eventPayload, params ITag[] tags) => 
        ResultBox.FromValue(FromValue(eventPayload, tags));

    /// <summary>
    /// Creates a ResultBox containing an event with tags
    /// </summary>
    public static ResultBox<EventOrNone> Event(IEventPayload eventPayload, IReadOnlyList<ITag> tags) => 
        ResultBox.FromValue(FromValue(eventPayload, tags));

    /// <summary>
    /// Gets the event payload value
    /// </summary>
    /// <exception cref="ResultsInvalidOperationException">Thrown when there is no event</exception>
    public IEventPayload GetEventPayload() =>
        HasEvent && EventPayload is not null 
            ? EventPayload 
            : throw new ResultsInvalidOperationException("No event payload available");

    /// <summary>
    /// Gets the tags
    /// </summary>
    /// <exception cref="ResultsInvalidOperationException">Thrown when there is no event</exception>
    public IReadOnlyList<ITag> GetTags() =>
        HasEvent && Tags is not null 
            ? Tags 
            : throw new ResultsInvalidOperationException("No tags available");

    /// <summary>
    /// Implicit conversion from UnitValue to represent no event
    /// </summary>
    public static implicit operator EventOrNone(UnitValue value) => Empty;

    /// <summary>
    /// Deconstructs the EventOrNone into its components
    /// </summary>
    public void Deconstruct(out IEventPayload? eventPayload, out IReadOnlyList<ITag>? tags)
    {
        eventPayload = EventPayload;
        tags = Tags;
    }
}