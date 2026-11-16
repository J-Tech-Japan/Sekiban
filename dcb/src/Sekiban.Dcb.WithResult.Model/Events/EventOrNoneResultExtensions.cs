using ResultBoxes;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Events;

/// <summary>
///     ResultBox-returning factory members for <see cref="EventOrNone" />, so WithResult command handlers keep
///     writing <c>EventOrNone.Event(...)</c> / <c>EventOrNone.None</c>.
/// </summary>
public static class EventOrNoneResultExtensions
{
    extension(EventOrNone)
    {
        /// <summary>
        ///     Represents no event (alias for <see cref="EventOrNone.Empty" />)
        /// </summary>
        public static ResultBox<EventOrNone> None => ResultBox.FromValue(EventOrNone.Empty);

        /// <summary>
        ///     Creates an EventOrNone from an event payload and tags (alias for <see cref="EventOrNone.From" />)
        /// </summary>
        public static ResultBox<EventOrNone> EventWithTags(IEventPayload eventPayload, params ITag[] tags) =>
            ResultBox.FromValue(EventOrNone.From(eventPayload, tags));

        /// <summary>
        ///     Creates an EventOrNone from EventPayloadWithTags
        /// </summary>
        public static ResultBox<EventOrNone> Event(EventPayloadWithTags eventPayloadWithTags) =>
            ResultBox.FromValue(EventOrNone.FromValue(eventPayloadWithTags));

        /// <summary>
        ///     Creates an EventOrNone from an event payload and a single tag
        /// </summary>
        public static ResultBox<EventOrNone> Event(IEventPayload eventPayload, ITag tag) =>
            ResultBox.FromValue(EventOrNone.From(eventPayload, tag));

        /// <summary>
        ///     Creates an EventOrNone from an event payload and two tags
        /// </summary>
        public static ResultBox<EventOrNone> Event(IEventPayload eventPayload, ITag tag1, ITag tag2) =>
            ResultBox.FromValue(EventOrNone.From(eventPayload, tag1, tag2));

        /// <summary>
        ///     Creates an EventOrNone from an event payload and three tags
        /// </summary>
        public static ResultBox<EventOrNone> Event(IEventPayload eventPayload, ITag tag1, ITag tag2, ITag tag3) =>
            ResultBox.FromValue(EventOrNone.From(eventPayload, tag1, tag2, tag3));
    }
}
