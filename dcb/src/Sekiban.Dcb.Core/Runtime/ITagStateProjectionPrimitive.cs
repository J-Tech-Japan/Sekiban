using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Runtime;

/// <summary>
///     Primitive abstraction for TagState projection.
///     Input/output contracts are fully serialized to support Native and WASM interchangeable implementations.
/// </summary>
public interface ITagStateProjectionPrimitive
{
    ITagStateProjectionAccumulator CreateAccumulator(TagStateId tagStateId);

    /// <summary>
    ///     Creates an accumulator, waiting asynchronously if the underlying WASM instance pool is at capacity.
    ///     Use this in async contexts (e.g., Orleans grains) to avoid blocking scheduler threads.
    ///     Default implementation delegates to the synchronous <see cref="CreateAccumulator"/>.
    /// </summary>
    ValueTask<ITagStateProjectionAccumulator> CreateAccumulatorAsync(TagStateId tagStateId, CancellationToken ct = default)
        => ValueTask.FromResult(CreateAccumulator(tagStateId));
}

/// <summary>
///     Stateful accumulator for TagState projection.
///     This allows callers to keep state in primitive-local memory while feeding state/events incrementally.
/// </summary>
public interface ITagStateProjectionAccumulator : IDisposable
{
    bool ApplyState(SerializableTagState? cachedState);
    bool ApplyEvents(
        IReadOnlyList<SerializableEvent> events,
        string? latestSortableUniqueId,
        CancellationToken cancellationToken = default);
    SerializableTagState GetSerializedState();
}
