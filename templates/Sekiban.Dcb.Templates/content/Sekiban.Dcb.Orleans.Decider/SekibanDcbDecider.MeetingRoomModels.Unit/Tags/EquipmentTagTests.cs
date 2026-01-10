using Dcb.MeetingRoomModels.Tags;

namespace SekibanDcbOrleans.MeetingRoomModels.Unit.Tags;

public class EquipmentTagTests
{
    [Fact]
    public void EquipmentTypeTag_Should_Create_And_Return_Id()
    {
        var id = Guid.NewGuid();
        var tag = new EquipmentTypeTag(id);

        Assert.Equal(id, tag.GetId());
        Assert.Equal("EquipmentType", EquipmentTypeTag.TagGroupName);
        Assert.True(tag.IsConsistencyTag());
    }

    [Fact]
    public void EquipmentTypeTag_FromContent_Should_Parse_Guid()
    {
        var id = Guid.NewGuid();
        var tag = EquipmentTypeTag.FromContent(id.ToString());

        Assert.Equal(id, tag.EquipmentTypeId);
    }

    [Fact]
    public void EquipmentItemTag_Should_Create_And_Return_Id()
    {
        var id = Guid.NewGuid();
        var tag = new EquipmentItemTag(id);

        Assert.Equal(id, tag.GetId());
        Assert.Equal("EquipmentItem", EquipmentItemTag.TagGroupName);
        Assert.True(tag.IsConsistencyTag());
    }

    [Fact]
    public void EquipmentItemTag_FromContent_Should_Parse_Guid()
    {
        var id = Guid.NewGuid();
        var tag = EquipmentItemTag.FromContent(id.ToString());

        Assert.Equal(id, tag.EquipmentItemId);
    }

    [Fact]
    public void EquipmentReservationTag_Should_Create_And_Return_Id()
    {
        var id = Guid.NewGuid();
        var tag = new EquipmentReservationTag(id);

        Assert.Equal(id, tag.GetId());
        Assert.Equal("EquipmentReservation", EquipmentReservationTag.TagGroupName);
        Assert.True(tag.IsConsistencyTag());
    }

    [Fact]
    public void EquipmentReservationTag_FromContent_Should_Parse_Guid()
    {
        var id = Guid.NewGuid();
        var tag = EquipmentReservationTag.FromContent(id.ToString());

        Assert.Equal(id, tag.EquipmentReservationId);
    }
}
