using Sekiban.Dcb.Tags;

namespace Dcb.MeetingRoomModels.Tags;

public record RoomReservationTag(Guid RoomId) : IGuidTagGroup<RoomReservationTag>
{
    public bool IsConsistencyTag() => true;
    public static string TagGroupName => "RoomReservation";
    public static RoomReservationTag FromContent(string content) => new(Guid.Parse(content));
    public Guid GetId() => RoomId;
}
