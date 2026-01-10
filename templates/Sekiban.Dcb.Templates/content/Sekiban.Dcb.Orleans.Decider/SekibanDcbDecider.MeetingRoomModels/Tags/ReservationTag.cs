using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.Tags;

public record ReservationTag(Guid ReservationId) : IGuidTagGroup<ReservationTag>
{
    public bool IsConsistencyTag() => true;
    public static string TagGroupName => "Reservation";
    public static ReservationTag FromContent(string content) => new(Guid.Parse(content));
    public Guid GetId() => ReservationId;
}
