using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.Events.Room;
using Dcb.MeetingRoomModels.Tags;

namespace SekibanDcbOrleans.MeetingRoomModels.Unit.Events;

public class EventTests
{
    [Fact]
    public void RoomCreated_Should_Have_RoomTag()
    {
        var roomId = Guid.NewGuid();
        var ev = new RoomCreated(roomId, "Conference Room A", 10, "Building 1", ["Projector", "Whiteboard"]);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Single(eventWithTags.Tags);
        var tag = Assert.IsType<RoomTag>(eventWithTags.Tags[0]);
        Assert.Equal(roomId, tag.RoomId);
    }

    [Fact]
    public void RoomUpdated_Should_Have_RoomTag()
    {
        var roomId = Guid.NewGuid();
        var ev = new RoomUpdated(roomId, "Updated Room", 15, "Building 2", ["Projector"]);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Single(eventWithTags.Tags);
        var tag = Assert.IsType<RoomTag>(eventWithTags.Tags[0]);
        Assert.Equal(roomId, tag.RoomId);
    }

    [Fact]
    public void ReservationDraftCreated_Should_Have_ReservationTag_And_RoomTag()
    {
        var reservationId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var organizerId = Guid.NewGuid();
        var ev = new ReservationDraftCreated(
            reservationId,
            roomId,
            organizerId,
            DateTime.UtcNow.AddHours(1),
            DateTime.UtcNow.AddHours(2),
            "Team Meeting");

        var eventWithTags = ev.GetEventWithTags();

        Assert.Equal(2, eventWithTags.Tags.Count);
        Assert.Contains(eventWithTags.Tags, t => t is ReservationTag rt && rt.ReservationId == reservationId);
        Assert.Contains(eventWithTags.Tags, t => t is RoomTag rt && rt.RoomId == roomId);
    }

    [Fact]
    public void ReservationHoldCommitted_Should_Have_ReservationTag_And_RoomTag()
    {
        var reservationId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var ev = new ReservationHoldCommitted(reservationId, roomId, false, null);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Equal(2, eventWithTags.Tags.Count);
        Assert.Contains(eventWithTags.Tags, t => t is ReservationTag rt && rt.ReservationId == reservationId);
        Assert.Contains(eventWithTags.Tags, t => t is RoomTag rt && rt.RoomId == roomId);
    }

    [Fact]
    public void ReservationConfirmed_Should_Have_ReservationTag_RoomTag_And_RoomDailyActivityTag()
    {
        var reservationId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var organizerId = Guid.NewGuid();
        var startTime = DateTime.UtcNow.AddHours(1);
        var endTime = DateTime.UtcNow.AddHours(2);
        var ev = new ReservationConfirmed(reservationId, roomId, organizerId, startTime, endTime, "Team Meeting", DateTime.UtcNow);

        var eventWithTags = ev.GetEventWithTags();

        // Should have ReservationTag, RoomTag, and RoomDailyActivityTag(s)
        Assert.True(eventWithTags.Tags.Count >= 3);
        Assert.Contains(eventWithTags.Tags, t => t is ReservationTag rt && rt.ReservationId == reservationId);
        Assert.Contains(eventWithTags.Tags, t => t is RoomTag rt && rt.RoomId == roomId);
        Assert.Contains(eventWithTags.Tags, t => t is RoomDailyActivityTag);
    }

    [Fact]
    public void ReservationCancelled_Should_Have_ReservationTag_RoomTag_And_RoomDailyActivityTag()
    {
        var reservationId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var startTime = DateTime.UtcNow.AddHours(1);
        var endTime = DateTime.UtcNow.AddHours(2);
        var ev = new ReservationCancelled(reservationId, roomId, startTime, endTime, "Changed plans", DateTime.UtcNow);

        var eventWithTags = ev.GetEventWithTags();

        // Should have ReservationTag, RoomTag, and RoomDailyActivityTag(s)
        Assert.True(eventWithTags.Tags.Count >= 3);
        Assert.Contains(eventWithTags.Tags, t => t is ReservationTag rt && rt.ReservationId == reservationId);
        Assert.Contains(eventWithTags.Tags, t => t is RoomTag rt && rt.RoomId == roomId);
        Assert.Contains(eventWithTags.Tags, t => t is RoomDailyActivityTag);
    }

    [Fact]
    public void ReservationRejected_Should_Have_ReservationTag_And_RoomTag()
    {
        var reservationId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var approvalRequestId = Guid.NewGuid();
        var ev = new ReservationRejected(reservationId, roomId, approvalRequestId, "Not approved", DateTime.UtcNow);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Equal(2, eventWithTags.Tags.Count);
    }

    [Fact]
    public void ApprovalFlowStarted_Should_Have_ApprovalRequestTag_And_ReservationTag()
    {
        var approvalRequestId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        var ev = new ApprovalFlowStarted(
            approvalRequestId,
            reservationId,
            roomId,
            requesterId,
            [approverId],
            DateTime.UtcNow);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Equal(2, eventWithTags.Tags.Count);
        Assert.Contains(eventWithTags.Tags, t => t is ApprovalRequestTag at && at.ApprovalRequestId == approvalRequestId);
        Assert.Contains(eventWithTags.Tags, t => t is ReservationTag rt && rt.ReservationId == reservationId);
    }

    [Fact]
    public void ApprovalDecisionRecorded_Should_Have_ApprovalRequestTag_And_ReservationTag()
    {
        var approvalRequestId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var approverId = Guid.NewGuid();
        var ev = new ApprovalDecisionRecorded(
            approvalRequestId,
            reservationId,
            approverId,
            ApprovalDecision.Approved,
            "Looks good",
            DateTime.UtcNow);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Equal(2, eventWithTags.Tags.Count);
        Assert.Contains(eventWithTags.Tags, t => t is ApprovalRequestTag at && at.ApprovalRequestId == approvalRequestId);
        Assert.Contains(eventWithTags.Tags, t => t is ReservationTag rt && rt.ReservationId == reservationId);
    }
}
