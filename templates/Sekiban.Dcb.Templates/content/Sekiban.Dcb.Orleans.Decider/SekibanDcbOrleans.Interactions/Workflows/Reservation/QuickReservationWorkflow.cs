using Dcb.EventSource.MeetingRoom.ApprovalRequest;
using Dcb.EventSource.MeetingRoom.Reservation;
using Dcb.EventSource.MeetingRoom.Room;
using Dcb.MeetingRoomModels.States.Room;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb;
namespace Dcb.Interactions.Workflows.Reservation;

/// <summary>
///     Result of the quick reservation workflow.
/// </summary>
public record QuickReservationResult(
    Guid ReservationId,
    string SortableUniqueId,
    bool RequiresApproval,
    Guid? ApprovalRequestId);

/// <summary>
///     Workflow for quick reservation: creates a draft, holds it, and confirms it in one step.
///     Use this for simple scenarios where no approval is needed.
/// </summary>
public class QuickReservationWorkflow(ISekibanExecutor executor)
{
    /// <summary>
    ///     Creates a reservation and immediately confirms it.
    /// </summary>
    /// <param name="roomId">The room to reserve</param>
    /// <param name="organizerId">The user making the reservation</param>
    /// <param name="startTime">Start time of the reservation</param>
    /// <param name="endTime">End time of the reservation</param>
    /// <param name="purpose">Purpose of the reservation</param>
    /// <param name="approvalRequestComment">Optional comment for approval request</param>
    /// <returns>The reservation result including ID and sortable unique ID</returns>
    public async Task<QuickReservationResult> ExecuteAsync(
        Guid roomId,
        Guid organizerId,
        string organizerName,
        DateTime startTime,
        DateTime endTime,
        string purpose,
        string? approvalRequestComment = null)
    {
        var reservationId = Guid.CreateVersion7();

        var roomState = await executor.GetTagStateAsync<RoomProjector>(new RoomTag(roomId));
        var roomPayload = roomState.Payload as RoomState ?? RoomState.Empty;

        // 1. Create the draft
        await executor.ExecuteAsync(new CreateReservationDraft
        {
            ReservationId = reservationId,
            RoomId = roomId,
            OrganizerId = organizerId,
            OrganizerName = organizerName,
            StartTime = startTime,
            EndTime = endTime,
            Purpose = purpose
        });

        Guid? approvalRequestId = null;

        if (roomPayload.RequiresApproval)
        {
            approvalRequestId = Guid.CreateVersion7();

            await executor.ExecuteAsync(new StartApprovalFlow
            {
                ApprovalRequestId = approvalRequestId.Value,
                ReservationId = reservationId,
                RoomId = roomId,
                RequesterId = organizerId,
                ApproverIds = [],
                RequestComment = approvalRequestComment
            });
        }

        // 2. Commit to held state
        var holdResult = await executor.ExecuteAsync(new CommitReservationHold
        {
            ReservationId = reservationId,
            RoomId = roomId,
            RequiresApproval = roomPayload.RequiresApproval,
            ApprovalRequestId = approvalRequestId
        });

        var sortableUniqueId = holdResult.SortableUniqueId ?? string.Empty;

        if (!roomPayload.RequiresApproval)
        {
            // 3. Confirm the reservation
            var confirmResult = await executor.ExecuteAsync(new ConfirmReservation
            {
                ReservationId = reservationId,
                RoomId = roomId
            });
            sortableUniqueId = confirmResult.SortableUniqueId ?? sortableUniqueId;
        }

        return new QuickReservationResult(reservationId, sortableUniqueId, roomPayload.RequiresApproval, approvalRequestId);
    }
}
