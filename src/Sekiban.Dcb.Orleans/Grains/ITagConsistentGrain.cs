using Orleans;
using ResultBoxes;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
/// Orleans grain interface for tag consistency management
/// Matches the interface of GeneralTagConsistentActor
/// </summary>
public interface ITagConsistentGrain : IGrainWithStringKey
{
    /// <summary>
    /// Gets the actor ID for this tag consistent actor
    /// </summary>
    Task<string> GetTagActorIdAsync();
    
    /// <summary>
    /// Gets the latest sortable unique ID known to this actor
    /// </summary>
    Task<ResultBox<string>> GetLatestSortableUniqueIdAsync();
    
    /// <summary>
    /// Make a reservation for a tag write
    /// </summary>
    Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string lastSortableUniqueId);
    
    /// <summary>
    /// Confirm a reservation
    /// </summary>
    Task<bool> ConfirmReservationAsync(TagWriteReservation reservation);
    
    /// <summary>
    /// Cancel a reservation
    /// </summary>
    Task<bool> CancelReservationAsync(TagWriteReservation reservation);
}