using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Runtime;

/// <summary>
///     Primitive abstraction for TagState projection.
///     Input/output contracts are fully serialized to support Native and WASM interchangeable implementations.
/// </summary>
public interface ITagStateProjectionPrimitive
{
    Task<ResultBox<SerializableTagState>> ProjectAsync(
        TagStateProjectionRequest request,
        CancellationToken cancellationToken = default);

    ITagStateProjectionAccumulator CreateAccumulator(TagStateId tagStateId);
}

/// <summary>
///     Stateful accumulator for TagState projection.
///     This allows callers to keep state in primitive-local memory while feeding state/events incrementally.
/// </summary>
public interface ITagStateProjectionAccumulator
{
    bool ApplyState(SerializableTagState? cachedState);
    bool ApplyEvents(
        IReadOnlyList<SerializableEvent> events,
        string? latestSortableUniqueId,
        CancellationToken cancellationToken = default);
    SerializableTagState GetSerializedState();
}
