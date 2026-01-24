using Dcb.MeetingRoomModels.Tags;

namespace SekibanDcbOrleans.MeetingRoomModels.Unit.Tags;

public class UserTagTests
{
    [Fact]
    public void UserTag_Should_Create_And_Return_Id()
    {
        var id = Guid.NewGuid();
        var tag = new UserTag(id);

        Assert.Equal(id, tag.GetId());
        Assert.Equal("User", UserTag.TagGroupName);
        Assert.True(tag.IsConsistencyTag());
    }

    [Fact]
    public void UserTag_FromContent_Should_Parse_Guid()
    {
        var id = Guid.NewGuid();
        var tag = UserTag.FromContent(id.ToString());

        Assert.Equal(id, tag.UserId);
    }

    [Fact]
    public void UserAccessTag_Should_Create_And_Return_Id()
    {
        var id = Guid.NewGuid();
        var tag = new UserAccessTag(id);

        Assert.Equal(id, tag.GetId());
        Assert.Equal("UserAccess", UserAccessTag.TagGroupName);
        Assert.True(tag.IsConsistencyTag());
    }

    [Fact]
    public void UserAccessTag_FromContent_Should_Parse_Guid()
    {
        var id = Guid.NewGuid();
        var tag = UserAccessTag.FromContent(id.ToString());

        Assert.Equal(id, tag.UserId);
    }

    [Fact]
    public void UserTags_With_Same_Id_Should_Be_Equal()
    {
        var id = Guid.NewGuid();
        var tag1 = new UserTag(id);
        var tag2 = new UserTag(id);

        Assert.Equal(tag1, tag2);
    }

    [Fact]
    public void UserAccessTags_With_Same_Id_Should_Be_Equal()
    {
        var id = Guid.NewGuid();
        var tag1 = new UserAccessTag(id);
        var tag2 = new UserAccessTag(id);

        Assert.Equal(tag1, tag2);
    }

    [Fact]
    public void UserTags_With_Different_Id_Should_Not_Be_Equal()
    {
        var tag1 = new UserTag(Guid.NewGuid());
        var tag2 = new UserTag(Guid.NewGuid());

        Assert.NotEqual(tag1, tag2);
    }
}
