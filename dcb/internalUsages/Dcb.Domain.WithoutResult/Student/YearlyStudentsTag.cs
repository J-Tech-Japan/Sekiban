using Sekiban.Dcb.Tags;
namespace Dcb.Domain.WithoutResult.Student;

public record YearlyStudentsTag(int Year) : IIntTagGroup<YearlyStudentsTag>
{
    public bool IsConsistencyTag() => true;
    public static string TagGroupName => "YearlyStudents";
    public int GetId() => Year;
    public static YearlyStudentsTag FromContent(string content) => new(int.Parse(content));
}