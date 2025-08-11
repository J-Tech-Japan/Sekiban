using Sekiban.Dcb.Tags;
namespace Dcb.Domain.ClassRoom;

public record ClassRoomTag(Guid ClassRoomId) : ITagGroup<ClassRoomTag>
{
    public bool IsConsistencyTag() => true;
    public string GetTagContent() => ClassRoomId.ToString();
    public static string GetTagGroupName() => "ClassRoom";
}
