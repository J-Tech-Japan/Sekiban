using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Tests for duplicate tag group registration detection
/// </summary>
public class TagGroupDuplicateRegistrationTests
{
    /// <summary>
    /// 同じグループ名の別 TagGroup 型を登録すると例外になることを検証します。
    /// </summary>
    [Fact]
    public void SimpleTagTypes_Should_Throw_When_Registering_Different_TagGroups_With_Same_Name()
    {
        // Arrange
        var tagTypes = new SimpleTagTypes();

        // Act - First registration
        tagTypes.RegisterTagGroupType<Namespace1.TestTagGroup>();

        // Act & Assert - Different type with same group name should throw
        var ex = Assert.Throws<InvalidOperationException>(() =>
            tagTypes.RegisterTagGroupType<Namespace2.TestTagGroup>());

        Assert.Contains("TestTagGroup", ex.Message);
        Assert.Contains("already", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 同じ TagGroup 型の再登録も禁止（初回のみ成功し2回目は例外）であることを検証します。
    /// </summary>
    [Fact]
    public void SimpleTagTypes_Should_Throw_When_Registering_Same_TagGroup_Twice()
    {
        var tagTypes = new SimpleTagTypes();
        tagTypes.RegisterTagGroupType<Namespace1.TestTagGroup>();
        var ex = Assert.Throws<InvalidOperationException>(() => tagTypes.RegisterTagGroupType<Namespace1.TestTagGroup>());
        Assert.Contains("TestTagGroup", ex.Message);
    }

    #region Test Tag Groups
    public static class Namespace1
    {
        public readonly record struct TestTagGroup(string Content) : ITagGroup<TestTagGroup>
        {
            public static string TagGroupName => "TestTagGroup";
            public static TestTagGroup FromContent(string content) => new(content);
            public string GetTagGroup() => TagGroupName;
            public string GetTag() => $"{TagGroupName}:{Content}";
            public string GetTagContent() => Content;
            public bool IsConsistencyTag() => false;
        }
    }

    public static class Namespace2
    {
        public readonly record struct TestTagGroup(string Content) : ITagGroup<TestTagGroup>
        {
            public static string TagGroupName => "TestTagGroup";
            public static TestTagGroup FromContent(string content) => new(content);
            public string GetTagGroup() => TagGroupName;
            public string GetTag() => $"{TagGroupName}:{Content}";
            public string GetTagContent() => Content;
            public bool IsConsistencyTag() => false;
        }
    }
    #endregion
}
