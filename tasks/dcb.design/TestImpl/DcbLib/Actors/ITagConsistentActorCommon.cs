using DcbLib.Tags;
using ResultBoxes;

namespace DcbLib.Actors;

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
    /// <returns>The latest sortable unique ID or empty string if none</returns>
    Task<string> GetLatestSortableUniqueIdAsync();
    
    Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string lastSortableUniqueId);
    Task<bool> ConfirmReservationAsync(TagWriteReservation reservation);
    Task<bool> CancelReservationAsync(TagWriteReservation reservation);
}