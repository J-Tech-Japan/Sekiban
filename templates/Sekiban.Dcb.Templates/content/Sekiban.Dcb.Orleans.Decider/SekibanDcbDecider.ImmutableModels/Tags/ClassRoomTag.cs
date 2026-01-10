using Sekiban.Dcb.Tags;
namespace Dcb.ImmutableModels.Tags;

public record ClassRoomTag(Guid ClassRoomId) : IGuidTagGroup<ClassRoomTag>
{
    public bool IsConsistencyTag() => true;
    public static string TagGroupName => "ClassRoom";
    public static ClassRoomTag FromContent(string content) => new(Guid.Parse(content));
    public Guid GetId() => ClassRoomId;
}
