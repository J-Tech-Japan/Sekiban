using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Orleans grain interface for tag consistency management
///     Matches the interface of GeneralTagConsistentActor
/// </summary>
public interface ITagConsistentGrain : IGrainWithStringKey
{
    /// <summary>
    ///     Gets the actor ID for this tag consistent actor
    /// </summary>
    Task<string> GetTagActorIdAsync();

    /// <summary>
    ///     Gets the latest sortable unique ID known to this actor. A failure of the actor (not initialized, catch-up
    ///     read failed) faults the returned task.
    /// </summary>
    Task<string> GetLatestSortableUniqueIdAsync();

    /// <summary>
    ///     Gets the latest sortable unique ID known to this actor, or <c>null</c> when the actor reports a failure (the
    ///     latest position is unknown). Only a failed grain call faults the returned task, so a caller can tell the
    ///     grain's own failure from a transport or timeout failure.
    /// </summary>
    Task<string?> TryGetLatestSortableUniqueIdAsync();

    /// <summary>
    ///     Make a reservation for a tag write. A rejected reservation (the tag is reserved or has been modified) faults
    ///     the returned task.
    /// </summary>
    Task<TagWriteReservation> MakeReservationAsync(string? lastSortableUniqueId);

    /// <summary>
    ///     Confirm a reservation
    /// </summary>
    Task<bool> ConfirmReservationAsync(TagWriteReservation reservation);

    /// <summary>
    ///     Cancel a reservation
    /// </summary>
    Task<bool> CancelReservationAsync(TagWriteReservation reservation);

    /// <summary>
    ///     Notifies the grain that an event was written with this tag.
    /// </summary>
    Task NotifyEventWrittenAsync();
}
