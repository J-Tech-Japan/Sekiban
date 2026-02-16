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
    /// <summary>
    ///     Applies a serialized snapshot envelope to the accumulator.
    ///     Returns <c>false</c> when snapshot cannot be applied (for example:
    ///     unsupported offloaded snapshot, projector identity/version mismatch, or deserialize failure).
    /// </summary>
    bool ApplySnapshot(SerializableMultiProjectionStateEnvelope? snapshot);

    /// <summary>
    ///     Applies serialized events incrementally.
    ///     Implementations must preserve deterministic ordering by <c>SortableUniqueId</c>,
    ///     and must not apply events newer than <paramref name="latestSortableUniqueId"/>
    ///     when it is provided.
    /// </summary>
    bool ApplyEvents(
        IReadOnlyList<SerializableEvent> events,
        string? latestSortableUniqueId,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the current serialized snapshot envelope.
    /// </summary>
    ResultBox<SerializableMultiProjectionStateEnvelope> GetSnapshot();

    /// <summary>
    ///     Gets projection metadata (safe/unsafe versions and positions) from current state.
    /// </summary>
    ResultBox<MultiProjectionStateMetadata> GetMetadata();
}
