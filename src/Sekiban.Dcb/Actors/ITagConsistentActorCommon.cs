using Sekiban.Dcb.Tags;
using ResultBoxes;

namespace Sekiban.Dcb.Actors;

public interface ITagConsistentActorCommon
{
    /// <summary>
    /// Gets the actor ID for this tag consistent actor
    /// Format: "[tagGroupName]:[tagContentName]"
    /// </summary>
    /// <returns>The actor ID string</returns>
    Task<string> GetTagActorIdAsync();
    
    /// <summary>
    /// Gets the latest sortable unique ID known to this actor
    /// Used by TagStateActor to determine the newest state without querying TagReader
    /// </summary>
    /// <returns>ResultBox containing the latest sortable unique ID (empty string if none) or error if something went wrong</returns>
    Task<ResultBox<string>> GetLatestSortableUniqueIdAsync();
    
    Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string lastSortableUniqueId);
    Task<bool> ConfirmReservationAsync(TagWriteReservation reservation);
    Task<bool> CancelReservationAsync(TagWriteReservation reservation);
}