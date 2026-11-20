using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using System.ComponentModel.DataAnnotations;
namespace Dcb.EventSource.MeetingRoom.Reservation;

public record RejectReservation : ICommandWithHandler<RejectReservation>
{
    [Required]
    public Guid ReservationId { get; init; }

    [Required]
    public Guid RoomId { get; init; }

    [Required]
    public Guid ApprovalRequestId { get; init; }

    [Required]
    [StringLength(500)]
    public string Reason { get; init; } = string.Empty;

    public static async Task<EventOrNone> HandleAsync(
        RejectReservation command,
        ICommandContext context)
    {
        var tag = new ReservationTag(command.ReservationId);
        var exists = await context.TagExistsAsync(tag);

        if (!exists)
        {
            throw new ApplicationException($"Reservation {command.ReservationId} not found");
        }


        var reservationState = await context.GetStateAsync<ReservationProjector>(tag);
        var payloadState = reservationState.Payload as ReservationState ?? ReservationState.Empty;

        // Only a held reservation that is waiting for approval can be rejected.
        // Drafts, confirmed, cancelled, rejected and expired reservations are not part of an approval decision.
        if (payloadState is not ReservationState.ReservationHeld { RequiresApproval: true } held)
        {
            throw new ApplicationException(
                $"Reservation {command.ReservationId} cannot be rejected from state: {payloadState.GetType().Name}");
        }

        if (held.ApprovalRequestId != command.ApprovalRequestId)
        {
            throw new ApplicationException(
                $"Approval request {command.ApprovalRequestId} does not belong to reservation {command.ReservationId}");
        }

        var monthlyTag = UserMonthlyReservationTag.FromStartTime(held.OrganizerId, held.StartTime);

        var payload = new ReservationRejected(
            command.ReservationId,
            held.RoomId,
            command.ApprovalRequestId,
            command.Reason,
            DateTime.UtcNow);

        var tags = new List<ITag>
        {
            new ReservationTag(command.ReservationId),
            new RoomReservationTag(held.RoomId),
            monthlyTag
        };

        return new EventPayloadWithTags(payload, tags);
    }
}
