using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.States.Reservation.Deciders;

namespace SekibanDcbOrleans.MeetingRoomModels.Unit.States;

public class ReservationDeciderTests
{
    private readonly Guid _reservationId = Guid.NewGuid();
    private readonly Guid _roomId = Guid.NewGuid();
    private readonly Guid _organizerId = Guid.NewGuid();
    private readonly DateTime _startTime = DateTime.UtcNow.AddHours(1);
    private readonly DateTime _endTime = DateTime.UtcNow.AddHours(2);

    [Fact]
    public void ReservationState_Empty_Should_Be_ReservationEmpty()
    {
        var empty = ReservationState.Empty;
        Assert.IsType<ReservationState.ReservationEmpty>(empty);
    }

    [Fact]
    public void ReservationDraftCreatedDecider_Should_Create_Draft()
    {
        var ev = new ReservationDraftCreated(
            _reservationId, _roomId, _organizerId, _startTime, _endTime, "Team Meeting");

        var state = ReservationDraftCreatedDecider.Create(ev);

        Assert.IsType<ReservationState.ReservationDraft>(state);
        Assert.Equal(_reservationId, state.ReservationId);
        Assert.Equal(_roomId, state.RoomId);
        Assert.Equal(_organizerId, state.OrganizerId);
        Assert.Equal("Team Meeting", state.Purpose);
    }

    [Fact]
    public void ReservationDraftCreatedDecider_Evolve_From_Empty_Should_Create_Draft()
    {
        var empty = ReservationState.Empty;
        var ev = new ReservationDraftCreated(
            _reservationId, _roomId, _organizerId, _startTime, _endTime, "Team Meeting");

        var state = empty.Evolve(ev);

        Assert.IsType<ReservationState.ReservationDraft>(state);
    }

    [Fact]
    public void ReservationDraftCreatedDecider_Evolve_From_NonEmpty_Should_Return_Same_State()
    {
        var existingDraft = new ReservationState.ReservationDraft(
            _reservationId, _roomId, _organizerId, _startTime, _endTime, "Existing");
        var ev = new ReservationDraftCreated(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), _startTime, _endTime, "New");

        var state = existingDraft.Evolve(ev);

        Assert.Same(existingDraft, state);
    }

    [Fact]
    public void ReservationHoldCommittedDecider_Should_Create_Held_Without_Approval()
    {
        var draft = new ReservationState.ReservationDraft(
            _reservationId, _roomId, _organizerId, _startTime, _endTime, "Team Meeting");
        var ev = new ReservationHoldCommitted(_reservationId, _roomId, false, null);

        var state = draft.Evolve(ev);

        var held = Assert.IsType<ReservationState.ReservationHeld>(state);
        Assert.Equal(_reservationId, held.ReservationId);
        Assert.False(held.RequiresApproval);
        Assert.Null(held.ApprovalRequestId);
    }

    [Fact]
    public void ReservationHoldCommittedDecider_Should_Create_Held_With_Approval()
    {
        var approvalId = Guid.NewGuid();
        var draft = new ReservationState.ReservationDraft(
            _reservationId, _roomId, _organizerId, _startTime, _endTime, "Team Meeting");
        var ev = new ReservationHoldCommitted(_reservationId, _roomId, true, approvalId);

        var state = draft.Evolve(ev);

        var held = Assert.IsType<ReservationState.ReservationHeld>(state);
        Assert.True(held.RequiresApproval);
        Assert.Equal(approvalId, held.ApprovalRequestId);
    }

    [Fact]
    public void ReservationConfirmedDecider_Should_Create_Confirmed()
    {
        var confirmTime = DateTime.UtcNow;
        var held = new ReservationState.ReservationHeld(
            _reservationId, _roomId, _organizerId, _startTime, _endTime, "Team Meeting", false, null);
        var ev = new ReservationConfirmed(_reservationId, _roomId, _organizerId, _startTime, _endTime, "Team Meeting", confirmTime);

        var state = held.Evolve(ev);

        var confirmed = Assert.IsType<ReservationState.ReservationConfirmed>(state);
        Assert.Equal(_reservationId, confirmed.ReservationId);
        Assert.Equal(confirmTime, confirmed.ConfirmedAt);
    }

    [Fact]
    public void ReservationCancelledDecider_Should_Cancel_From_Draft()
    {
        var cancelTime = DateTime.UtcNow;
        var draft = new ReservationState.ReservationDraft(
            _reservationId, _roomId, _organizerId, _startTime, _endTime, "Team Meeting");
        var ev = new ReservationCancelled(_reservationId, _roomId, _startTime, _endTime, "Changed plans", cancelTime);

        var state = draft.Evolve(ev);

        var cancelled = Assert.IsType<ReservationState.ReservationCancelled>(state);
        Assert.Equal(_reservationId, cancelled.ReservationId);
        Assert.Equal("Changed plans", cancelled.Reason);
    }

    [Fact]
    public void ReservationCancelledDecider_Should_Cancel_From_Held()
    {
        var cancelTime = DateTime.UtcNow;
        var held = new ReservationState.ReservationHeld(
            _reservationId, _roomId, _organizerId, _startTime, _endTime, "Team Meeting", false, null);
        var ev = new ReservationCancelled(_reservationId, _roomId, _startTime, _endTime, "Changed plans", cancelTime);

        var state = held.Evolve(ev);

        Assert.IsType<ReservationState.ReservationCancelled>(state);
    }

    [Fact]
    public void ReservationCancelledDecider_Should_Cancel_From_Confirmed()
    {
        var cancelTime = DateTime.UtcNow;
        var confirmed = new ReservationState.ReservationConfirmed(
            _reservationId, _roomId, _organizerId, _startTime, _endTime, "Team Meeting", DateTime.UtcNow.AddMinutes(-5));
        var ev = new ReservationCancelled(_reservationId, _roomId, _startTime, _endTime, "Changed plans", cancelTime);

        var state = confirmed.Evolve(ev);

        Assert.IsType<ReservationState.ReservationCancelled>(state);
    }

    [Fact]
    public void ReservationRejectedDecider_Should_Reject_From_Held_Requiring_Approval()
    {
        var approvalId = Guid.NewGuid();
        var rejectTime = DateTime.UtcNow;
        var held = new ReservationState.ReservationHeld(
            _reservationId, _roomId, _organizerId, _startTime, _endTime, "Team Meeting", true, approvalId);
        var ev = new ReservationRejected(_reservationId, _roomId, approvalId, "Not approved", rejectTime);

        var state = held.Evolve(ev);

        var rejected = Assert.IsType<ReservationState.ReservationRejected>(state);
        Assert.Equal(_reservationId, rejected.ReservationId);
        Assert.Equal(approvalId, rejected.ApprovalRequestId);
        Assert.Equal("Not approved", rejected.Reason);
    }

    [Fact]
    public void ReservationRejectedDecider_Should_Not_Reject_From_Held_Not_Requiring_Approval()
    {
        var rejectTime = DateTime.UtcNow;
        var held = new ReservationState.ReservationHeld(
            _reservationId, _roomId, _organizerId, _startTime, _endTime, "Team Meeting", false, null);
        var ev = new ReservationRejected(_reservationId, _roomId, Guid.NewGuid(), "Not approved", rejectTime);

        var state = held.Evolve(ev);

        Assert.Same(held, state); // Should return same state (idempotency)
    }

    [Fact]
    public void Full_Reservation_Lifecycle_Without_Approval()
    {
        // Start with empty
        ReservationState state = ReservationState.Empty;
        Assert.IsType<ReservationState.ReservationEmpty>(state);

        // Create draft
        state = state.Evolve(new ReservationDraftCreated(
            _reservationId, _roomId, _organizerId, _startTime, _endTime, "Team Meeting"));
        Assert.IsType<ReservationState.ReservationDraft>(state);

        // Commit hold
        state = state.Evolve(new ReservationHoldCommitted(_reservationId, _roomId, false, null));
        Assert.IsType<ReservationState.ReservationHeld>(state);

        // Confirm
        state = state.Evolve(new ReservationConfirmed(_reservationId, _roomId, _organizerId, _startTime, _endTime, "Team Meeting", DateTime.UtcNow));
        Assert.IsType<ReservationState.ReservationConfirmed>(state);
    }

    [Fact]
    public void Full_Reservation_Lifecycle_With_Cancellation()
    {
        // Start with empty
        ReservationState state = ReservationState.Empty;

        // Create draft
        state = state.Evolve(new ReservationDraftCreated(
            _reservationId, _roomId, _organizerId, _startTime, _endTime, "Team Meeting"));

        // Commit hold
        state = state.Evolve(new ReservationHoldCommitted(_reservationId, _roomId, false, null));

        // Cancel
        state = state.Evolve(new ReservationCancelled(_reservationId, _roomId, _startTime, _endTime, "Changed plans", DateTime.UtcNow));
        Assert.IsType<ReservationState.ReservationCancelled>(state);
    }
}
