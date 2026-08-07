using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.TestSupport.ExecutedUser;

/// <summary>Tag group used by executed-user provider scenario tests.</summary>
public sealed record TestTag : ITagGroup<TestTag>
{
    private readonly Guid _id;
    public TestTag(Guid id) => _id = id;
    public bool IsConsistencyTag() => false;
    public static string TagGroupName => "Test";
    public string GetTag() => $"Test:{_id}";
    public string GetTagContent() => _id.ToString();
    public static TestTag FromContent(string content) => new(Guid.Parse(content));
}
