using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Commands;

/// <summary>
///     Wraps an <see cref="ICommandContext"/> and rethrows failures by unwrapping <see cref="ResultBox{T}"/> values.
///     Used by <see cref="ISekibanExecutorWithoutResult"/> flows so handlers can work without ResultBox plumbing.
/// </summary>
public sealed class CommandContextWithoutResultAdapter : ICommandContextWithoutResult
{
    private readonly ICommandContext _inner;

    public CommandContextWithoutResultAdapter(ICommandContext inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public Task<TagStateTyped<TState>> GetStateAsync<TState, TProjector>(ITag tag)
        where TState : ITagStatePayload
        where TProjector : ITagProjector<TProjector> =>
        _inner.GetStateAsync<TState, TProjector>(tag).UnwrapBox();

    public Task<TagState> GetStateAsync<TProjector>(ITag tag) where TProjector : ITagProjector<TProjector> =>
        _inner.GetStateAsync<TProjector>(tag).UnwrapBox();

    public Task<bool> TagExistsAsync(ITag tag) =>
        _inner.TagExistsAsync(tag).UnwrapBox();

    public Task<string> GetTagLatestSortableUniqueIdAsync(ITag tag) =>
        _inner.GetTagLatestSortableUniqueIdAsync(tag).UnwrapBox();

    public Task<EventOrNone> AppendEvent(IEventPayload ev, params ITag[] tags) =>
        _inner.AppendEvent(ev, tags).UnwrapBox();
}
