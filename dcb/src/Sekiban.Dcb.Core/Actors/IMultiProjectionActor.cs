using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.Actors;

/// <summary>
///     Projection actor abstraction used by Orleans grains.
/// </summary>
public interface IMultiProjectionActor
{
    Task AddEventsAsync(IReadOnlyList<Event> events, bool finishedCatchUp = true, EventSource source = EventSource.Unknown);

    Task AddSerializableEventsAsync(IReadOnlyList<SerializableEvent> events, bool finishedCatchUp = true);

    Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true);

    Task<ResultBox<SerializableMultiProjectionStateEnvelope>> GetSnapshotAsync(
        bool canGetUnsafeState = true,
        CancellationToken cancellationToken = default);

    Task SetSnapshotAsync(SerializableMultiProjectionStateEnvelope envelope, CancellationToken cancellationToken = default);

    Task SetCurrentState(SerializableMultiProjectionState state);

    Task SetCurrentStateIgnoringVersion(SerializableMultiProjectionState state);

    Task<string> GetSafeLastSortableUniqueIdAsync();

    SortableUniqueId PeekCurrentSafeWindowThreshold();

    Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId);
}
