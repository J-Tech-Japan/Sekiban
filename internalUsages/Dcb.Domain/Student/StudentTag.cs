using Sekiban.Dcb.Tags;

namespace Dcb.Domain.Student;

public record StudentTag(Guid StudentId) : ITag
{
    public bool IsConsistencyTag() => true;
    public string GetTagGroup() => "Student";
    public string GetTagContent() => StudentId.ToString();
}