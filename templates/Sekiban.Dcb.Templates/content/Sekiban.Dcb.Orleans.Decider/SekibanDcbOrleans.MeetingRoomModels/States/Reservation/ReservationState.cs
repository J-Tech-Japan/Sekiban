using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.States.Reservation;

/// <summary>
///     Reservation state using discriminated union pattern.
///     Each nested record represents a distinct state in the reservation lifecycle.
/// </summary>
public abstract record ReservationState : ITagStatePayload
{
    public static ReservationState Empty => new ReservationEmpty();

    /// <summary>
    ///     Empty/initial state before reservation is created
    /// </summary>
    public record ReservationEmpty() : ReservationState;

    /// <summary>
    ///     Draft state - reservation is being prepared
    /// </summary>
    public record ReservationDraft(
        Guid ReservationId,
        Guid RoomId,
        Guid OrganizerId,
        DateTime StartTime,
        DateTime EndTime,
        string Purpose) : ReservationState;

    /// <summary>
    ///     Held state - reservation is committed but may need approval
    /// </summary>
    public record ReservationHeld(
        Guid ReservationId,
        Guid RoomId,
        Guid OrganizerId,
        DateTime StartTime,
        DateTime EndTime,
        string Purpose,
        bool RequiresApproval,
        Guid? ApprovalRequestId) : ReservationState;

    /// <summary>
    ///     Confirmed state - reservation is final and active
    /// </summary>
    public record ReservationConfirmed(
        Guid ReservationId,
        Guid RoomId,
        Guid OrganizerId,
        DateTime StartTime,
        DateTime EndTime,
        string Purpose,
        DateTime ConfirmedAt) : ReservationState;

    /// <summary>
    ///     Cancelled state - reservation was cancelled
    /// </summary>
    public record ReservationCancelled(
        Guid ReservationId,
        Guid RoomId,
        string Reason,
        DateTime CancelledAt) : ReservationState;

    /// <summary>
    ///     Rejected state - reservation was rejected during approval
    /// </summary>
    public record ReservationRejected(
        Guid ReservationId,
        Guid RoomId,
        Guid ApprovalRequestId,
        string Reason,
        DateTime RejectedAt) : ReservationState;

    /// <summary>
    ///     Expired state - reservation hold or approval expired
    /// </summary>
    public record ReservationExpired(
        Guid ReservationId,
        Guid RoomId,
        string ExpiredReason,
        DateTime ExpiredAt) : ReservationState;
}
