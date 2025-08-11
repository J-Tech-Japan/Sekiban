using Sekiban.Dcb.Tags;
namespace Dcb.Domain.Student;

public record StudentTag(Guid StudentId) : ITagGroup<StudentTag>
{
    public bool IsConsistencyTag() => true;
    public static string GetTagGroupName() => "Student";
    public string GetTagContent() => StudentId.ToString();
}
