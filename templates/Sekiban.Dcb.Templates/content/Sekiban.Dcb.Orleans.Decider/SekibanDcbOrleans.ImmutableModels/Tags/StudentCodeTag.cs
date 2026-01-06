using Sekiban.Dcb.Tags;
namespace Dcb.ImmutableModels.Tags;

public record StudentCodeTag(string StudentCode) : IStringTagGroup<StudentCodeTag>
{
    public bool IsConsistencyTag() => false;
    public static string TagGroupName => "StudentCode";
    public string GetId() => StudentCode;
    public static StudentCodeTag FromContent(string content) => new(content);
}
