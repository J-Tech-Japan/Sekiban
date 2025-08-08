using Sekiban.Dcb.Tags;
namespace Dcb.Domain.ClassRoom;

public record ClassRoomTag(Guid ClassRoomId) : ITag
{
    public bool IsConsistencyTag() => true;
    public string GetTagGroup() => "ClassRoom";
    public string GetTagContent() => ClassRoomId.ToString();
}
