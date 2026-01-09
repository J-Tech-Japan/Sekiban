using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Tags;

namespace SekibanDcbOrleans.MeetingRoomModels.Unit.Tags;

public class TagTests
{
    [Fact]
    public void RoomTag_Should_Create_And_Return_Id()
    {
        var id = Guid.NewGuid();
        var tag = new RoomTag(id);

        Assert.Equal(id, tag.GetId());
        Assert.Equal("Room", RoomTag.TagGroupName);
        Assert.True(tag.IsConsistencyTag());
    }

    [Fact]
    public void RoomTag_FromContent_Should_Parse_Guid()
    {
        var id = Guid.NewGuid();
        var tag = RoomTag.FromContent(id.ToString());

        Assert.Equal(id, tag.RoomId);
    }

    [Fact]
    public void ReservationTag_Should_Create_And_Return_Id()
    {
        var id = Guid.NewGuid();
        var tag = new ReservationTag(id);

        Assert.Equal(id, tag.GetId());
        Assert.Equal("Reservation", ReservationTag.TagGroupName);
        Assert.True(tag.IsConsistencyTag());
    }

    [Fact]
    public void ReservationTag_FromContent_Should_Parse_Guid()
    {
        var id = Guid.NewGuid();
        var tag = ReservationTag.FromContent(id.ToString());

        Assert.Equal(id, tag.ReservationId);
    }

    [Fact]
    public void ApprovalRequestTag_Should_Create_And_Return_Id()
    {
        var id = Guid.NewGuid();
        var tag = new ApprovalRequestTag(id);

        Assert.Equal(id, tag.GetId());
        Assert.Equal("ApprovalRequest", ApprovalRequestTag.TagGroupName);
        Assert.True(tag.IsConsistencyTag());
    }

    [Fact]
    public void ApprovalRequestTag_FromContent_Should_Parse_Guid()
    {
        var id = Guid.NewGuid();
        var tag = ApprovalRequestTag.FromContent(id.ToString());

        Assert.Equal(id, tag.ApprovalRequestId);
    }

    [Fact]
    public void Tags_With_Same_Id_Should_Be_Equal()
    {
        var id = Guid.NewGuid();
        var tag1 = new RoomTag(id);
        var tag2 = new RoomTag(id);

        Assert.Equal(tag1, tag2);
    }

    [Fact]
    public void Tags_With_Different_Id_Should_Not_Be_Equal()
    {
        var tag1 = new RoomTag(Guid.NewGuid());
        var tag2 = new RoomTag(Guid.NewGuid());

        Assert.NotEqual(tag1, tag2);
    }

    [Fact]
    public void UserMonthlyReservationTag_Should_RoundTrip_Content()
    {
        var userId = Guid.NewGuid();
        var month = new DateOnly(2026, 1, 1);
        var tag = new UserMonthlyReservationTag(userId, month);

        Assert.Equal("UserMonthlyReservation", UserMonthlyReservationTag.TagGroupName);
        var otherTag = new UserMonthlyReservationTag(userId, month);
        Assert.Equal(tag.GetId(), otherTag.GetId());

        var content = ((ITag)tag).GetTagContent();
        var parsed = UserMonthlyReservationTag.FromContent(content);
        Assert.Equal(tag.GetId(), parsed.GetId());
    }
}
