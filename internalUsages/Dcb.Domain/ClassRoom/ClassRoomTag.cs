using Sekiban.Dcb.Tags;
namespace Dcb.Domain.ClassRoom;

public record ClassRoomTag(Guid ClassRoomId) : ITagGroup<ClassRoomTag>
{
    public bool IsConsistencyTag() => true;
    public string GetTagContent() => ClassRoomId.ToString();
    public static string TagGroupName => "ClassRoom";
    /// <summary>
    /// content 文字列 (Guid) からタグインスタンスを生成します。
    /// </summary>
    public static ClassRoomTag FromContent(string content) => new(Guid.Parse(content));
}
