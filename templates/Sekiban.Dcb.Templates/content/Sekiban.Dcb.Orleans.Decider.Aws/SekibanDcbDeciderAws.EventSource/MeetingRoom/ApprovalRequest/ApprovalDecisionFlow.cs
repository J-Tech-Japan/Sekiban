using Dcb.EventSource.MeetingRoom.Reservation;
using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Dcb.MeetingRoomModels.States.ApprovalRequest;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;

namespace Dcb.EventSource.MeetingRoom.ApprovalRequest;

/// <summary>
///     Records an approval decision and moves the reservation in the same command.
///     Both the approval request and the reservation are read through the command context, so a
///     concurrent change to either (for example a cancellation) makes the write fail instead of
///     leaving a decision recorded against a reservation that can no longer follow it.
/// </summary>
internal static class ApprovalDecisionFlow
{
    internal static async Task<EventOrNone> DecideAsync(
        ICommandContext context,
        Guid approvalRequestId,
        Guid reservationId,
        Guid approverId,
        ApprovalDecision decision,
        string? comment)
    {
        var approvalTag = new ApprovalRequestTag(approvalRequestId);
        if (!await context.TagExistsAsync(approvalTag))
        {
            throw new ApplicationException($"ApprovalRequest {approvalRequestId} not found");
        }

        var approvalState = await context.GetStateAsync<ApprovalRequestProjector>(approvalTag);
        if (approvalState.Payload is not ApprovalRequestState.ApprovalRequestPending pending)
        {
            throw new ApplicationException($"ApprovalRequest {approvalRequestId} is not pending");
        }

        if (pending.ReservationId != reservationId)
        {
            throw new ApplicationException($"Reservation {reservationId} does not match approval request");
        }

        if (pending.ApproverIds.Count > 0 && !pending.ApproverIds.Contains(approverId))
        {
            throw new ApplicationException($"User {approverId} is not an authorized approver");
        }

        var reservationTag = new ReservationTag(reservationId);
        if (!await context.TagExistsAsync(reservationTag))
        {
            throw new ApplicationException($"Reservation {reservationId} not found");
        }

        var reservationState = await context.GetStateAsync<ReservationProjector>(reservationTag);
        if (reservationState.Payload is not ReservationState.ReservationHeld { RequiresApproval: true } held)
        {
            var stateName = reservationState.Payload.GetType().Name;
            throw new ApplicationException(
                $"Reservation {reservationId} is not awaiting approval (state: {stateName})");
        }

        if (held.ApprovalRequestId != approvalRequestId)
        {
            throw new ApplicationException(
                $"Approval request {approvalRequestId} does not belong to reservation {reservationId}");
        }

        // Build the reservation transition first: a confirmation can still fail on a schedule conflict,
        // and then nothing is written.
        var transition = decision switch
        {
            ApprovalDecision.Approved => await ReservationTransitions.ConfirmHeldAsync(context, held, comment),
            ApprovalDecision.Rejected => ReservationTransitions.RejectHeld(
                held, approvalRequestId, string.IsNullOrWhiteSpace(comment) ? "Rejected" : comment),
            _ => throw new ApplicationException($"Unknown approval decision {decision}")
        };

        await context.AppendEvent(new ApprovalDecisionRecorded(
            approvalRequestId,
            reservationId,
            approverId,
            decision,
            comment,
            DateTime.UtcNow).GetEventWithTags());
        return await context.AppendEvent(transition);
    }
}
