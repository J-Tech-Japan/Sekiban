using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Multi-projection actor contract for applying events and retrieving/storing state.
/// </summary>
public interface IMultiProjectionActorCommon
{
    /// <summary>
    ///     Apply a batch of events to the current projector state.
    /// </summary>
    /// <param name="events">Events to apply.</param>
    /// <param name="finishedCatchUp">
    ///     Whether the event stream has finished catching up.
    ///     When false, the actor is in catching up state and data is incomplete.
    /// </param>
    Task AddEventsAsync(IReadOnlyList<Event> events, bool finishedCatchUp = true);

    /// <summary>
    ///     Apply a batch of serialized events to the current projector state.
    /// </summary>
    /// <param name="events">Events to apply.</param>
    /// <param name="finishedCatchUp">
    ///     Whether the event stream has finished catching up.
    ///     When false, the actor is in catching up state and data is incomplete.
    /// </param>
    Task AddSerializableEventsAsync(IReadOnlyList<SerializableEvent> events, bool finishedCatchUp = true);

    /// <summary>
    ///     Get the current in-memory state of the projector.
    /// </summary>
    /// <param name="canGetUnsafeState">
    ///     Whether to allow returning unsafe state.
    ///     When false, only safe state is returned.
    /// </param>
    Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true);

    /// <summary>
    ///     Get the current state serialized to bytes for transport or storage.
    /// </summary>
    /// <param name="canGetUnsafeState">
    ///     Whether to allow returning unsafe state.
    ///     When false, only safe state (suitable for snapshots) is returned.
    /// </param>
    Task<ResultBox<SerializableMultiProjectionState>> GetSerializableStateAsync(bool canGetUnsafeState = true);

    /// <summary>
    ///     Overwrite current state from a serialized snapshot.
    /// </summary>
    Task SetCurrentState(SerializableMultiProjectionState state);
}
