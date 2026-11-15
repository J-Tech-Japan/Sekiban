using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     Core interface for command context that provides access to tag states during command processing.
///     This context is created by the CommandExecutor and passed to handlers.
///     Failures are thrown as exceptions; expected outcomes (a tag without events) are values.
/// </summary>
public interface ICoreCommandContext
{
    /// <summary>
    ///     Gets the current state for a specific tag using the specified projector
    /// </summary>
    /// <typeparam name="TState">The type of state payload expected</typeparam>
    /// <typeparam name="TProjector">The type of projector to use</typeparam>
    /// <param name="tag">The tag to query</param>
    /// <returns>The TagStateTyped</returns>
    /// <exception cref="Exception">Thrown when state retrieval fails</exception>
    Task<TagStateTyped<TState>> GetStateAsync<TState, TProjector>(ITag tag) where TState : ITagStatePayload
        where TProjector : ITagProjector<TProjector>;

    /// <summary>
    ///     Gets the current state for a specific tag using the specified projector, returning TagState
    /// </summary>
    /// <typeparam name="TProjector">The type of projector to use</typeparam>
    /// <param name="tag">The tag to query</param>
    /// <returns>The TagState</returns>
    /// <exception cref="Exception">Thrown when state retrieval fails</exception>
    Task<TagState> GetStateAsync<TProjector>(ITag tag) where TProjector : ITagProjector<TProjector>;

    /// <summary>
    ///     Checks if a tag exists (has any events)
    /// </summary>
    /// <param name="tag">The tag to check</param>
    /// <returns>True if the tag has associated events, false if not</returns>
    /// <exception cref="Exception">Thrown when the check fails</exception>
    Task<bool> TagExistsAsync(ITag tag);

    /// <summary>
    ///     Gets the latest sortable unique ID for a tag (for optimistic concurrency)
    /// </summary>
    /// <param name="tag">The tag to query</param>
    /// <returns>The latest sortable unique ID, or an empty string if the tag has no events</returns>
    /// <exception cref="Exception">Thrown when retrieval fails</exception>
    Task<string> GetTagLatestSortableUniqueIdAsync(ITag tag);

    /// <summary>
    ///     Appends an event with tags to the context
    /// </summary>
    /// <param name="ev">The event with tags to append</param>
    /// <param name="tags"></param>
    /// <returns>EventOrNone representing the appended event</returns>
    /// <exception cref="Exception">Thrown when appending fails</exception>
    Task<EventOrNone> AppendEvent(IEventPayload ev, params ITag[] tags);
    /// <summary>
    ///     Appends an event with tags to the context
    /// </summary>
    /// <param name="eventPayloadWithTags"></param>
    /// <returns>EventOrNone representing the appended event</returns>
    /// <exception cref="Exception">Thrown when appending fails</exception>
    Task<EventOrNone> AppendEvent(EventPayloadWithTags eventPayloadWithTags);
}
