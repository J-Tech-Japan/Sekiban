using Dcb.MeetingRoomModels.Events.EquipmentItem;
using Dcb.MeetingRoomModels.Events.EquipmentReservation;
using Dcb.MeetingRoomModels.Events.EquipmentType;
using Dcb.MeetingRoomModels.Tags;

namespace SekibanDcbOrleans.MeetingRoomModels.Unit.Events;

public class EquipmentEventTests
{
    [Fact]
    public void EquipmentTypeCreated_Should_Have_EquipmentTypeTag()
    {
        var typeId = Guid.NewGuid();
        var ev = new EquipmentTypeCreated(typeId, "Microphone", "Wireless microphone", 5, 2);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Single(eventWithTags.Tags);
        var tag = Assert.IsType<EquipmentTypeTag>(eventWithTags.Tags[0]);
        Assert.Equal(typeId, tag.EquipmentTypeId);
    }

    [Fact]
    public void EquipmentTypeUpdated_Should_Have_EquipmentTypeTag()
    {
        var typeId = Guid.NewGuid();
        var ev = new EquipmentTypeUpdated(typeId, "Updated Name", "Updated Desc", 3);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Single(eventWithTags.Tags);
        var tag = Assert.IsType<EquipmentTypeTag>(eventWithTags.Tags[0]);
        Assert.Equal(typeId, tag.EquipmentTypeId);
    }

    [Fact]
    public void EquipmentItemRegistered_Should_Have_ItemTag_And_TypeTag()
    {
        var itemId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        var ev = new EquipmentItemRegistered(itemId, typeId, "MIC-001", null, DateTime.UtcNow);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Equal(2, eventWithTags.Tags.Count);
        Assert.Contains(eventWithTags.Tags, t => t is EquipmentItemTag it && it.EquipmentItemId == itemId);
        Assert.Contains(eventWithTags.Tags, t => t is EquipmentTypeTag tt && tt.EquipmentTypeId == typeId);
    }

    [Fact]
    public void EquipmentItemRetired_Should_Have_ItemTag_And_TypeTag()
    {
        var itemId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        var ev = new EquipmentItemRetired(itemId, typeId, "Broken", DateTime.UtcNow);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Equal(2, eventWithTags.Tags.Count);
    }

    [Fact]
    public void EquipmentReservationCreated_Without_RoomReservation_Should_Have_ReservationTag_And_TypeTag()
    {
        var reservationId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var ev = new EquipmentReservationCreated(
            reservationId, typeId, 2, null, requesterId, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2));

        var eventWithTags = ev.GetEventWithTags();

        Assert.Equal(2, eventWithTags.Tags.Count);
        Assert.Contains(eventWithTags.Tags, t => t is EquipmentReservationTag rt && rt.EquipmentReservationId == reservationId);
        Assert.Contains(eventWithTags.Tags, t => t is EquipmentTypeTag tt && tt.EquipmentTypeId == typeId);
    }

    [Fact]
    public void EquipmentReservationCreated_With_RoomReservation_Should_Have_Three_Tags()
    {
        var reservationId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        var roomReservationId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var ev = new EquipmentReservationCreated(
            reservationId, typeId, 2, roomReservationId, requesterId, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2));

        var eventWithTags = ev.GetEventWithTags();

        Assert.Equal(3, eventWithTags.Tags.Count);
        Assert.Contains(eventWithTags.Tags, t => t is EquipmentReservationTag rt && rt.EquipmentReservationId == reservationId);
        Assert.Contains(eventWithTags.Tags, t => t is EquipmentTypeTag tt && tt.EquipmentTypeId == typeId);
        Assert.Contains(eventWithTags.Tags, t => t is ReservationTag rrt && rrt.ReservationId == roomReservationId);
    }

    [Fact]
    public void EquipmentItemAssigned_Should_Have_ReservationTag_And_ItemTag()
    {
        var reservationId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var ev = new EquipmentItemAssigned(reservationId, itemId, DateTime.UtcNow);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Equal(2, eventWithTags.Tags.Count);
        Assert.Contains(eventWithTags.Tags, t => t is EquipmentReservationTag rt && rt.EquipmentReservationId == reservationId);
        Assert.Contains(eventWithTags.Tags, t => t is EquipmentItemTag it && it.EquipmentItemId == itemId);
    }

    [Fact]
    public void EquipmentCheckedOut_Should_Have_ReservationTag_And_ItemTag()
    {
        var reservationId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var ev = new EquipmentCheckedOut(reservationId, itemId, userId, DateTime.UtcNow);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Equal(2, eventWithTags.Tags.Count);
    }

    [Fact]
    public void EquipmentReturned_Should_Have_ReservationTag_And_ItemTag()
    {
        var reservationId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var ev = new EquipmentReturned(reservationId, itemId, userId, DateTime.UtcNow, "Good");

        var eventWithTags = ev.GetEventWithTags();

        Assert.Equal(2, eventWithTags.Tags.Count);
    }

    [Fact]
    public void EquipmentReservationCancelled_Should_Have_ReservationTag()
    {
        var reservationId = Guid.NewGuid();
        var ev = new EquipmentReservationCancelled(reservationId, "No longer needed", DateTime.UtcNow);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Single(eventWithTags.Tags);
        var tag = Assert.IsType<EquipmentReservationTag>(eventWithTags.Tags[0]);
        Assert.Equal(reservationId, tag.EquipmentReservationId);
    }
}
