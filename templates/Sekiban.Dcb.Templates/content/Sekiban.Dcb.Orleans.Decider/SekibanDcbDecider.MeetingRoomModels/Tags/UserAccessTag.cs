using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.Tags;

public record UserAccessTag(Guid UserId) : IGuidTagGroup<UserAccessTag>
{
    public bool IsConsistencyTag() => true;
    public static string TagGroupName => "UserAccess";
    public static UserAccessTag FromContent(string content) => new(Guid.Parse(content));
    public Guid GetId() => UserId;
}
