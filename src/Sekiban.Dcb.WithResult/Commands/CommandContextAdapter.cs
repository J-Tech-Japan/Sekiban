using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     Adapter that wraps ICoreCommandContext and implements ICommandContext
///     This allows WithResult to use Core's context implementation
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
        _core.GetStateAsync<TState, TProjector>(tag);

    public Task<ResultBox<TagState>> GetStateAsync<TProjector>(ITag tag)
        where TProjector : ITagProjector<TProjector> =>
        _core.GetStateAsync<TProjector>(tag);

    public Task<ResultBox<bool>> TagExistsAsync(ITag tag) =>
        _core.TagExistsAsync(tag);

    public Task<ResultBox<string>> GetTagLatestSortableUniqueIdAsync(ITag tag) =>
        _core.GetTagLatestSortableUniqueIdAsync(tag);

    public Task<ResultBox<EventOrNone>> AppendEvent(IEventPayload ev, params ITag[] tags) =>
        _core.AppendEvent(ev, tags);
}
