using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     Adapter that wraps the exception-based ICoreCommandContext and implements the ResultBox-based ICommandContext.
///     A failure thrown by the core context arrives as a failed box carrying that exception.
/// </summary>
internal class CommandContextAdapter : ICommandContext
{
    private readonly ICoreCommandContext _core;

    public CommandContextAdapter(ICoreCommandContext core)
    {
        _core = core ?? throw new ArgumentNullException(nameof(core));
    }

    public Task<ResultBox<TagStateTyped<TState>>> GetStateAsync<TState, TProjector>(ITag tag)
        where TState : ITagStatePayload
        where TProjector : ITagProjector<TProjector> =>
        ResultBox.WrapTry(() => _core.GetStateAsync<TState, TProjector>(tag));

    public Task<ResultBox<TagState>> GetStateAsync<TProjector>(ITag tag)
        where TProjector : ITagProjector<TProjector> =>
        ResultBox.WrapTry(() => _core.GetStateAsync<TProjector>(tag));

    public Task<ResultBox<bool>> TagExistsAsync(ITag tag) =>
        ResultBox.WrapTry(() => _core.TagExistsAsync(tag));

    public Task<ResultBox<string>> GetTagLatestSortableUniqueIdAsync(ITag tag) =>
        ResultBox.WrapTry(() => _core.GetTagLatestSortableUniqueIdAsync(tag));

    public Task<ResultBox<EventOrNone>> AppendEvent(IEventPayload ev, params ITag[] tags) =>
        ResultBox.WrapTry(() => _core.AppendEvent(ev, tags));

    public Task<ResultBox<EventOrNone>> AppendEvent(EventPayloadWithTags eventPayloadWithTags)
     => ResultBox.WrapTry(() => _core.AppendEvent(eventPayloadWithTags));
}
