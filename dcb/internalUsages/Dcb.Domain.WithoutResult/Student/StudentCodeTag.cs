using Sekiban.Dcb.Tags;
namespace Dcb.Domain.WithoutResult.Student;

public record StudentCodeTag(string StudentCode) : IStringTagGroup<StudentCodeTag>
{
    public bool IsConsistencyTag() => false;
    public static string TagGroupName => "StudentCode";
    public string GetId() => StudentCode;
    public static StudentCodeTag FromContent(string content) => new(content);
}