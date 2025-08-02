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
    string GetTagActorId();
    
    ResultBox<TagWriteReservation> MakeReservation(string lastSortableUniqueId);
    bool ConfirmReservation(TagWriteReservation reservation);
    bool CancelReservation(TagWriteReservation reservation);
}