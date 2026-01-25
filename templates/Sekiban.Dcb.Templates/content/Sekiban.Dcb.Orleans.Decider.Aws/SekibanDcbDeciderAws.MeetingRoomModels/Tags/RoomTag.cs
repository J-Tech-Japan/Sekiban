using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.Tags;

public record RoomTag(Guid RoomId) : IGuidTagGroup<RoomTag>
{
    public bool IsConsistencyTag() => true;
    public static string TagGroupName => "Room";
    public static RoomTag FromContent(string content) => new(Guid.Parse(content));
    public Guid GetId() => RoomId;
}
