using Sekiban.Dcb.Tags;
namespace Dcb.Domain.Student;

public record StudentTag(Guid StudentId) : ITagGroup<StudentTag>
{
    public bool IsConsistencyTag() => true;
    public static string TagGroupName => "Student";
    public string GetTagContent() => StudentId.ToString();
    /// <summary>
    /// content 文字列 (Guid) からタグインスタンスを生成します。
    /// </summary>
    public static StudentTag FromContent(string content) => new(Guid.Parse(content));
}
