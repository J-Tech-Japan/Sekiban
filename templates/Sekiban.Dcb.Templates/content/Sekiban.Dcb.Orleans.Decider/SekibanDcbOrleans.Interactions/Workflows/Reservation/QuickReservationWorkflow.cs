using Dcb.EventSource.MeetingRoom.Reservation;
using Sekiban.Dcb;
namespace Dcb.Interactions.Workflows.Reservation;

/// <summary>
///     Result of the quick reservation workflow.
/// </summary>
public record QuickReservationResult(Guid ReservationId, string SortableUniqueId);

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
    /// <returns>The reservation result including ID and sortable unique ID</returns>
    public async Task<QuickReservationResult> ExecuteAsync(
        Guid roomId,
        Guid organizerId,
        DateTime startTime,
        DateTime endTime,
        string purpose)
    {
        var reservationId = Guid.CreateVersion7();

        // 1. Create the draft
        await executor.ExecuteAsync(new CreateReservationDraft
        {
            ReservationId = reservationId,
            RoomId = roomId,
            OrganizerId = organizerId,
            StartTime = startTime,
            EndTime = endTime,
            Purpose = purpose
        });

        // 2. Commit to held state (no approval required)
        await executor.ExecuteAsync(new CommitReservationHold
        {
            ReservationId = reservationId,
            RoomId = roomId,
            RequiresApproval = false,
            ApprovalRequestId = null
        });

        // 3. Confirm the reservation
        var confirmResult = await executor.ExecuteAsync(new ConfirmReservation
        {
            ReservationId = reservationId,
            RoomId = roomId
        });

        return new QuickReservationResult(reservationId, confirmResult.SortableUniqueId ?? string.Empty);
    }
}
