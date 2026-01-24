using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.Tags;

public record UserTag(Guid UserId) : IGuidTagGroup<UserTag>
{
    public bool IsConsistencyTag() => true;
    public static string TagGroupName => "User";
    public static UserTag FromContent(string content) => new(Guid.Parse(content));
    public Guid GetId() => UserId;
}
