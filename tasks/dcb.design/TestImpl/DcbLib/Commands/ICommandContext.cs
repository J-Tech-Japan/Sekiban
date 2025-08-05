using DcbLib.Tags;
using DcbLib.Events;
using ResultBoxes;

namespace DcbLib.Commands;

/// <summary>
/// Provides access to tag states during command processing.
/// This context is created by the CommandExecutor and passed to handlers.
/// </summary>
public interface ICommandContext
{
    /// <summary>
    /// Gets the current state for a specific tag using the specified projector
    /// </summary>
    /// <typeparam name="TState">The type of state payload expected</typeparam>
    /// <typeparam name="TProjector">The type of projector to use</typeparam>
    /// <param name="tag">The tag to query</param>
    /// <returns>ResultBox containing the TagStateTyped or error information</returns>
    Task<ResultBox<TagStateTyped<TState>>> GetStateAsync<TState, TProjector>(ITag tag) 
        where TState : ITagStatePayload
        where TProjector : ITagProjector, new();

    /// <summary>
    /// Gets the current state for a specific tag using the specified projector, returning TagState
    /// </summary>
    /// <typeparam name="TProjector">The type of projector to use</typeparam>
    /// <param name="tag">The tag to query</param>
    /// <returns>ResultBox containing the TagState or error information</returns>
    Task<ResultBox<TagState>> GetStateAsync<TProjector>(ITag tag) 
        where TProjector : ITagProjector, new();

    /// <summary>
    /// Checks if a tag exists (has any events)
    /// </summary>
    /// <param name="tag">The tag to check</param>
    /// <returns>True if the tag has associated events</returns>
    Task<bool> TagExistsAsync(ITag tag);

    /// <summary>
    /// Gets the latest sortable unique ID for a tag (for optimistic concurrency)
    /// </summary>
    /// <param name="tag">The tag to query</param>
    /// <returns>The latest sortable unique ID or empty string if not found</returns>
    Task<string> GetTagLatestSortableUniqueIdAsync(ITag tag);

    /// <summary>
    /// Appends an event with tags to the context
    /// </summary>
    /// <param name="ev">The event with tags to append</param>
    /// <returns>ResultBox containing EventOrNone representing the appended event</returns>
    ResultBox<EventOrNone> AppendEvent(EventPayloadWithTags ev);
}