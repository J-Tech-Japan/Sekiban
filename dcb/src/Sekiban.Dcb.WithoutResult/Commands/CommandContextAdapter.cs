using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     Adapter that wraps ICoreCommandContext and implements ICommandContext (exception-based)
///     This allows WithoutResult to use Core's context implementation
/// </summary>
internal class CommandContextAdapter : ICommandContext
{
    private readonly ICoreCommandContext _core;

    public CommandContextAdapter(ICoreCommandContext core)
    {
        _core = core ?? throw new ArgumentNullException(nameof(core));
    }

    public async Task<TagStateTyped<TState>> GetStateAsync<TState, TProjector>(ITag tag)
        where TState : ITagStatePayload
        where TProjector : ITagProjector<TProjector>
    {
        var result = await _core.GetStateAsync<TState, TProjector>(tag);
        return result.UnwrapBox();
    }

    public async Task<TagState> GetStateAsync<TProjector>(ITag tag)
        where TProjector : ITagProjector<TProjector>
    {
        var result = await _core.GetStateAsync<TProjector>(tag);
        return result.UnwrapBox();
    }

    public async Task<bool> TagExistsAsync(ITag tag)
    {
        var result = await _core.TagExistsAsync(tag);
        return result.UnwrapBox();
    }

    public async Task<string> GetTagLatestSortableUniqueIdAsync(ITag tag)
    {
        var result = await _core.GetTagLatestSortableUniqueIdAsync(tag);
        return result.UnwrapBox();
    }

    public async Task<EventOrNone> AppendEvent(IEventPayload ev, params ITag[] tags)
    {
        var result = await _core.AppendEvent(ev, tags);
        return result.UnwrapBox();
    }

    public async Task<EventOrNone> AppendEvent(EventPayloadWithTags eventPayloadWithTags)
    {
        return await _core.AppendEvent(eventPayloadWithTags).UnwrapBox();
    }
}
