using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.Runtime;

/// <summary>
///     Primitive abstraction for MultiProjection projection.
///     Input/output contracts are fully serialized to support Native and WASM interchangeable implementations.
/// </summary>
public interface IMultiProjectionProjectionPrimitive
{
    IMultiProjectionProjectionAccumulator CreateAccumulator(
        string projectorName,
        string projectorVersion,
        GeneralMultiProjectionActorOptions? options = null,
        ILogger? logger = null);
}

/// <summary>
///     Stateful accumulator for MultiProjection projection.
///     This allows callers to keep state in primitive-local memory while feeding state/events incrementally.
/// </summary>
public interface IMultiProjectionProjectionAccumulator
{
    bool ApplySnapshot(SerializableMultiProjectionStateEnvelope? snapshot);
    bool ApplyEvents(
        IReadOnlyList<SerializableEvent> events,
        string? latestSortableUniqueId,
        CancellationToken cancellationToken = default);
    ResultBox<SerializableMultiProjectionStateEnvelope> GetSnapshot();
    ResultBox<MultiProjectionStateMetadata> GetMetadata();
}
