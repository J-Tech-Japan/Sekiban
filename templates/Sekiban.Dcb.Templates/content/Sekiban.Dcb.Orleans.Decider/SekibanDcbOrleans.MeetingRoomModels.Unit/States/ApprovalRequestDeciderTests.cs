using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Dcb.MeetingRoomModels.States.ApprovalRequest;
using Dcb.MeetingRoomModels.States.ApprovalRequest.Deciders;

namespace SekibanDcbOrleans.MeetingRoomModels.Unit.States;

public class ApprovalRequestDeciderTests
{
    private readonly Guid _approvalRequestId = Guid.NewGuid();
    private readonly Guid _reservationId = Guid.NewGuid();
    private readonly Guid _roomId = Guid.NewGuid();
    private readonly Guid _requesterId = Guid.NewGuid();
    private readonly Guid _approverId = Guid.NewGuid();
    private const string RequestComment = "Need approval";

    [Fact]
    public void ApprovalRequestState_Empty_Should_Be_ApprovalRequestEmpty()
    {
        var empty = ApprovalRequestState.Empty;
        Assert.IsType<ApprovalRequestState.ApprovalRequestEmpty>(empty);
    }

    [Fact]
    public void ApprovalFlowStartedDecider_Should_Create_Pending()
    {
        var requestTime = DateTime.UtcNow;
        var ev = new ApprovalFlowStarted(
            _approvalRequestId, _reservationId, _roomId, _requesterId, [_approverId], requestTime, RequestComment);

        var state = ApprovalFlowStartedDecider.Create(ev);

        Assert.IsType<ApprovalRequestState.ApprovalRequestPending>(state);
        Assert.Equal(_approvalRequestId, state.ApprovalRequestId);
        Assert.Equal(_reservationId, state.ReservationId);
        Assert.Equal(_roomId, state.RoomId);
        Assert.Equal(_requesterId, state.RequesterId);
        Assert.Single(state.ApproverIds);
        Assert.Contains(_approverId, state.ApproverIds);
        Assert.Equal(RequestComment, state.RequestComment);
    }

    [Fact]
    public void ApprovalFlowStartedDecider_Evolve_From_Empty_Should_Create_Pending()
    {
        var empty = ApprovalRequestState.Empty;
        var ev = new ApprovalFlowStarted(
            _approvalRequestId, _reservationId, _roomId, _requesterId, [_approverId], DateTime.UtcNow, RequestComment);

        var state = empty.Evolve(ev);

        Assert.IsType<ApprovalRequestState.ApprovalRequestPending>(state);
    }

    [Fact]
    public void ApprovalDecisionRecordedDecider_Should_Create_Approved()
    {
        var decisionTime = DateTime.UtcNow;
        var pending = new ApprovalRequestState.ApprovalRequestPending(
            _approvalRequestId, _reservationId, _roomId, _requesterId, [_approverId], DateTime.UtcNow.AddMinutes(-5), RequestComment);
        var ev = new ApprovalDecisionRecorded(
            _approvalRequestId, _reservationId, _approverId, ApprovalDecision.Approved, "Looks good", decisionTime);

        var state = pending.Evolve(ev);

        var approved = Assert.IsType<ApprovalRequestState.ApprovalRequestApproved>(state);
        Assert.Equal(_approvalRequestId, approved.ApprovalRequestId);
        Assert.Equal(_reservationId, approved.ReservationId);
        Assert.Equal(_roomId, approved.RoomId);
        Assert.Equal(_requesterId, approved.RequesterId);
        Assert.Single(approved.ApproverIds);
        Assert.Contains(_approverId, approved.ApproverIds);
        Assert.Equal(_approverId, approved.ApproverId);
        Assert.Equal("Looks good", approved.Comment);
        Assert.Equal(decisionTime, approved.DecidedAt);
        Assert.Equal(RequestComment, approved.RequestComment);
    }

    [Fact]
    public void ApprovalDecisionRecordedDecider_Should_Create_Rejected()
    {
        var decisionTime = DateTime.UtcNow;
        var pending = new ApprovalRequestState.ApprovalRequestPending(
            _approvalRequestId, _reservationId, _roomId, _requesterId, [_approverId], DateTime.UtcNow.AddMinutes(-5), RequestComment);
        var ev = new ApprovalDecisionRecorded(
            _approvalRequestId, _reservationId, _approverId, ApprovalDecision.Rejected, "Not appropriate", decisionTime);

        var state = pending.Evolve(ev);

        var rejected = Assert.IsType<ApprovalRequestState.ApprovalRequestRejected>(state);
        Assert.Equal(_approvalRequestId, rejected.ApprovalRequestId);
        Assert.Equal(_reservationId, rejected.ReservationId);
        Assert.Equal(_roomId, rejected.RoomId);
        Assert.Equal(_requesterId, rejected.RequesterId);
        Assert.Single(rejected.ApproverIds);
        Assert.Contains(_approverId, rejected.ApproverIds);
        Assert.Equal(_approverId, rejected.ApproverId);
        Assert.Equal("Not appropriate", rejected.Comment);
        Assert.Equal(decisionTime, rejected.DecidedAt);
        Assert.Equal(RequestComment, rejected.RequestComment);
    }

    [Fact]
    public void ApprovalDecisionRecordedDecider_From_NonPending_Should_Return_Same_State()
    {
        var approved = new ApprovalRequestState.ApprovalRequestApproved(
            _approvalRequestId,
            _reservationId,
            _roomId,
            _requesterId,
            [_approverId],
            _approverId,
            "Already approved",
            DateTime.UtcNow.AddMinutes(-5),
            RequestComment);
        var ev = new ApprovalDecisionRecorded(
            _approvalRequestId, _reservationId, _approverId, ApprovalDecision.Rejected, "Changed mind", DateTime.UtcNow);

        var state = approved.Evolve(ev);

        Assert.Same(approved, state); // Idempotency: can't change approved state
    }

    [Fact]
    public void ApprovalDecisionRecordedDecider_Validate_Should_Throw_For_Unauthorized_Approver()
    {
        var unauthorizedApproverId = Guid.NewGuid();
        var pending = new ApprovalRequestState.ApprovalRequestPending(
            _approvalRequestId, _reservationId, _roomId, _requesterId, [_approverId], DateTime.UtcNow, RequestComment);

        var ex = Assert.Throws<InvalidOperationException>(() => pending.Validate(unauthorizedApproverId));
        Assert.Contains("not an authorized approver", ex.Message);
    }

    [Fact]
    public void ApprovalDecisionRecordedDecider_Validate_Should_Pass_For_Authorized_Approver()
    {
        var pending = new ApprovalRequestState.ApprovalRequestPending(
            _approvalRequestId, _reservationId, _roomId, _requesterId, [_approverId], DateTime.UtcNow, RequestComment);

        var exception = Record.Exception(() => pending.Validate(_approverId));
        Assert.Null(exception);
    }

    [Fact]
    public void Full_ApprovalRequest_Lifecycle_Approved()
    {
        // Start with empty
        ApprovalRequestState state = ApprovalRequestState.Empty;
        Assert.IsType<ApprovalRequestState.ApprovalRequestEmpty>(state);

        // Start approval flow
        state = state.Evolve(new ApprovalFlowStarted(
            _approvalRequestId, _reservationId, _roomId, _requesterId, [_approverId], DateTime.UtcNow, RequestComment));
        Assert.IsType<ApprovalRequestState.ApprovalRequestPending>(state);

        // Approve
        state = state.Evolve(new ApprovalDecisionRecorded(
            _approvalRequestId, _reservationId, _approverId, ApprovalDecision.Approved, "Approved", DateTime.UtcNow));
        Assert.IsType<ApprovalRequestState.ApprovalRequestApproved>(state);
    }

    [Fact]
    public void Full_ApprovalRequest_Lifecycle_Rejected()
    {
        // Start with empty
        ApprovalRequestState state = ApprovalRequestState.Empty;

        // Start approval flow
        state = state.Evolve(new ApprovalFlowStarted(
            _approvalRequestId, _reservationId, _roomId, _requesterId, [_approverId], DateTime.UtcNow, RequestComment));

        // Reject
        state = state.Evolve(new ApprovalDecisionRecorded(
            _approvalRequestId, _reservationId, _approverId, ApprovalDecision.Rejected, "Rejected", DateTime.UtcNow));
        Assert.IsType<ApprovalRequestState.ApprovalRequestRejected>(state);
    }
}
