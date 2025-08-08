using System;
using System.Threading.Tasks;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Actors
{
    /// <summary>
    /// Multi-projection actor contract for applying events and retrieving/storing state.
    /// </summary>
    public interface IMultiProjectionActorCommon
    {
        /// <summary>
        /// Apply a batch of events to the current projector state.
        /// </summary>
        /// <param name="events">Events to apply.</param>
        Task AddEventsAsync(System.Collections.Generic.IReadOnlyList<Event> events);

    /// <summary>
    /// Apply a batch of serialized events to the current projector state.
    /// </summary>
    Task AddSerializableEventsAsync(System.Collections.Generic.IReadOnlyList<Sekiban.Dcb.Events.SerializableEvent> events);

        /// <summary>
        /// Get the current in-memory state of the projector.
        /// </summary>
        Task<ResultBox<MultiProjections.MultiProjectionState>> GetStateAsync();

    /// <summary>
    /// Get the current state serialized to bytes for transport or storage.
    /// </summary>
    Task<ResultBox<SerializableMultiProjectionState>> GetSerializableStateAsync();

        /// <summary>
        /// Overwrite current state from a serialized snapshot.
        /// </summary>
        Task SetCurrentState(SerializableMultiProjectionState state);
    }
}
