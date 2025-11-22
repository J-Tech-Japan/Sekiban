using Dcb.Domain;
using Dcb.Domain.ClassRoom;
using Dcb.Domain.Student;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     TagTypes (SimpleTagTypes) の復元動作テスト。
/// </summary>
public class TagTypesTests
{
    [Fact]
    public void GetTag_Should_Return_StudentTag()
    {
        var domain = DomainType.GetDomainTypes();
        var id = Guid.NewGuid();
        var tagString = $"Student:{id}";
        var tag = domain.TagTypes.GetTag(tagString);
        Assert.IsType<StudentTag>(tag);
        Assert.Equal(id.ToString(), tag.GetTagContent());
    }

    [Fact]
    public void GetTag_Should_Return_ClassRoomTag()
    {
        var domain = DomainType.GetDomainTypes();
        var id = Guid.NewGuid();
        var tagString = $"ClassRoom:{id}";
        var tag = domain.TagTypes.GetTag(tagString);
        Assert.IsType<ClassRoomTag>(tag);
        Assert.Equal(id.ToString(), tag.GetTagContent());
    }

    [Fact]
    public void GetTag_Should_Fallback_When_Unknown_Group()
    {
        var domain = DomainType.GetDomainTypes();
        var tag = domain.TagTypes.GetTag("Unknown:123");
        Assert.IsType<FallbackTag>(tag);
        Assert.Equal("Unknown", tag.GetTagGroup());
        Assert.Equal("123", tag.GetTagContent());
    }

    [Fact]
    public void GetTag_Should_Fallback_When_Invalid_Format()
    {
        var domain = DomainType.GetDomainTypes();
        var tag = domain.TagTypes.GetTag("NoColonHere");
        Assert.IsType<FallbackTag>(tag);
        Assert.Equal("", tag.GetTagGroup());
        Assert.Equal("NoColonHere", tag.GetTagContent());
    }
}
