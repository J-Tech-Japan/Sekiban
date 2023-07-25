using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
namespace Sekiban.Core.Query.MultiProjections.Projections;

/// <summary>
///     Multi Projection Projection container for the cache.
/// </summary>
/// <typeparam name="TProjection"></typeparam>
/// <typeparam name="TProjectionPayload"></typeparam>
// ReSharper disable once UnusedTypeParameter
public record MultipleMemoryProjectionContainer<TProjection, TProjectionPayload> where TProjection : IMultiProjector<TProjectionPayload>, new()
    where TProjectionPayload : IMultiProjectionPayloadCommon, new()
{
    /// <summary>
    ///     Unsafe (could be changed order) events.
    /// </summary>
    public List<IEvent> UnsafeEvents { get; init; } = new();

    /// <summary>
    ///     State for current. Could retrieve updated events with unsafe events.
    /// </summary>
    public MultiProjectionState<TProjectionPayload>? State { get; init; } = default;
    /// <summary>
    ///     Safe State (should not updated by unsafe events).
    /// </summary>
    public MultiProjectionState<TProjectionPayload>? SafeState { get; init; } = default;
    /// <summary>
    ///     Last sortable unique id. (include unsafe events)
    /// </summary>
    public SortableUniqueIdValue? LastSortableUniqueId { get; init; } = null;
    /// <summary>
    ///     Last safe sortable unique id. (exclude unsafe events)
    /// </summary>
    public SortableUniqueIdValue? SafeSortableUniqueId { get; init; } = null;
}
