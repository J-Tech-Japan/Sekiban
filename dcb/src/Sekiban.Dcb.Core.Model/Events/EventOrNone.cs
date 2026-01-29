using ResultBoxes;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Events;

/// <summary>
///     Represents either an event with tags or no event.
///     Used in command handlers to indicate optional event emission.
/// </summary>
public record EventOrNone(EventPayloadWithTags? EventPayloadWithTags, bool HasEvent)
{
    /// <summary>
    ///     Represents no event
    /// </summary>
    public static EventOrNone Empty => new(default, false);

    /// <summary>
    ///     Creates an EventOrNone from an EventPayloadWithTags
    /// </summary>
    public static EventOrNone FromValue(EventPayloadWithTags eventWithTags) =>
        new(eventWithTags, true);

    /// <summary>
    ///     Creates an EventOrNone from an event payload and tags
    /// </summary>
    public static EventOrNone From(IEventPayload eventPayload, params ITag[] tags) =>
        new(new EventPayloadWithTags(eventPayload, tags.ToList()), true);

    /// <summary>
    ///     Creates an EventOrNone from an event payload and a list of tags
    /// </summary>
    public static EventOrNone FromValue(IEventPayload eventPayload, List<ITag> tags) =>
        new(new EventPayloadWithTags(eventPayload, tags), true);

    /// <summary>
    ///     Gets the EventPayloadWithTags value
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when there is no event</exception>
    public EventPayloadWithTags GetValue() =>
        HasEvent && EventPayloadWithTags is not null
            ? EventPayloadWithTags
            : throw new InvalidOperationException("No value");

    /// <summary>
    ///     Represents no event (alias for Empty) - Returns ResultBox for WithResult compatibility
    /// </summary>
    public static ResultBox<EventOrNone> None => ResultBox.FromValue(Empty);

    /// <summary>
    ///     Creates an EventOrNone from an event payload and tags (alias for From) - Returns ResultBox for WithResult compatibility
    /// </summary>
    public static ResultBox<EventOrNone> EventWithTags(IEventPayload eventPayload, params ITag[] tags) =>
        ResultBox.FromValue(From(eventPayload, tags));

    /// <summary>
    ///     Creates an EventOrNone from EventPayloadWithTags - Returns ResultBox for WithResult compatibility
    /// </summary>
    public static ResultBox<EventOrNone> Event(EventPayloadWithTags eventPayloadWithTags) =>
        ResultBox.FromValue(FromValue(eventPayloadWithTags));

    /// <summary>
    ///     Creates an EventOrNone from an event payload and a single tag - Returns ResultBox for WithResult compatibility
    /// </summary>
    public static ResultBox<EventOrNone> Event(IEventPayload eventPayload, ITag tag) =>
        ResultBox.FromValue(From(eventPayload, tag));

    /// <summary>
    ///     Creates an EventOrNone from an event payload and two tags - Returns ResultBox for WithResult compatibility
    /// </summary>
    public static ResultBox<EventOrNone> Event(IEventPayload eventPayload, ITag tag1, ITag tag2) =>
        ResultBox.FromValue(From(eventPayload, tag1, tag2));

    /// <summary>
    ///     Creates an EventOrNone from an event payload and three tags - Returns ResultBox for WithResult compatibility
    /// </summary>
    public static ResultBox<EventOrNone> Event(IEventPayload eventPayload, ITag tag1, ITag tag2, ITag tag3) =>
        ResultBox.FromValue(From(eventPayload, tag1, tag2, tag3));

    /// <summary>
    ///     Implicit conversion from EventPayloadWithTags to EventOrNone
    /// </summary>
    public static implicit operator EventOrNone(EventPayloadWithTags eventPayloadWithTags) =>
        FromValue(eventPayloadWithTags);
}
