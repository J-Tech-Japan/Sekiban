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
    ///     Implicit conversion from EventPayloadWithTags to EventOrNone
    /// </summary>
    public static implicit operator EventOrNone(EventPayloadWithTags eventPayloadWithTags) =>
        FromValue(eventPayloadWithTags);
}
