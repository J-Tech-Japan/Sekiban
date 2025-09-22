using Sekiban.Dcb.Tags;
namespace Dcb.Domain.Student;

public record StudentTag(Guid StudentId) : IGuidTagGroup<StudentTag>
{
    public bool IsConsistencyTag() => true;
    public static string TagGroupName => "Student";
    public string GetTagContent() => StudentId.ToString();
    /// <summary>
    ///     content 文字列 (Guid) からタグインスタンスを生成します。
    /// </summary>
    public static StudentTag FromContent(string content) => new(Guid.Parse(content));

    /// <summary>
    ///     Get the GUID identifier for this tag
    /// </summary>
    public Guid GetId() => StudentId;
}

public record YearlyStudentsTag(int Year) : IIntTagGroup<YearlyStudentsTag>
{

    public bool IsConsistencyTag() => true;
    public string GetTagContent() => Year.ToString();
    public static string TagGroupName => "YearlyStudents";
    public int GetId() => Year;
    static YearlyStudentsTag ITagGroup<YearlyStudentsTag>.FromContent(string content) => new(int.Parse(content));
}

public record StudentCodeTag(string StudentCode) : IStringTagGroup<StudentCodeTag>
{
    public bool IsConsistencyTag() => false;
    public string GetTagContent() => StudentCode;
    public static string TagGroupName => "StudentCode";
    public string GetId() => StudentCode;
    static StudentCodeTag ITagGroup<StudentCodeTag>.FromContent(string content) => new(content);
}