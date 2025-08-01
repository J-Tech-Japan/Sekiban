using DcbLib.Tags;
using ResultBoxes;

namespace DcbLib.Actors;

public interface ITagConsistentActorCommon
{
    ResultBox<TagWriteReservation> MakeReservation(string lastSortableUniqueId);
    bool ConfirmReservation(TagWriteReservation reservation);
    bool CancelReservation(TagWriteReservation reservation);
}