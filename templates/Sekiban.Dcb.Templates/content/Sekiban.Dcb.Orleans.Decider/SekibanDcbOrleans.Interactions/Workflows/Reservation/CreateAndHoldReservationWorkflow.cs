using Dcb.EventSource.MeetingRoom.Reservation;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb;
namespace Dcb.Interactions.Workflows.Reservation;

/// <summary>
///     Workflow to create a reservation draft and immediately commit it to held state.
///     This is a convenience workflow for simple reservation scenarios that don't require
///     a separate draft review step.
/// </summary>
public class CreateAndHoldReservationWorkflow(ISekibanExecutor executor)
{
    /// <summary>
    ///     Creates a reservation and commits it to held state.
    /// </summary>
    /// <param name="roomId">The room to reserve</param>
    /// <param name="organizerId">The user making the reservation</param>
    /// <param name="startTime">Start time of the reservation</param>
    /// <param name="endTime">End time of the reservation</param>
    /// <param name="purpose">Purpose of the reservation</param>
    /// <param name="requiresApproval">Whether the reservation requires approval</param>
    /// <param name="approvalRequestComment">Optional comment for approval request</param>
    /// <returns>The reservation ID</returns>
    public async Task<Guid> ExecuteAsync(
        Guid roomId,
        Guid organizerId,
        string organizerName,
        DateTime startTime,
        DateTime endTime,
        string purpose,
        bool requiresApproval = false,
        string? approvalRequestComment = null)
    {
        var reservationId = Guid.CreateVersion7();

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

        // 2. Commit to held state
        await executor.ExecuteAsync(new CommitReservationHold
        {
            ReservationId = reservationId,
            RoomId = roomId,
            RequiresApproval = requiresApproval,
            ApprovalRequestId = null,
            ApprovalRequestComment = approvalRequestComment
        });

        return reservationId;
    }
}
