using System.Text.Json.Serialization;
using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.States.Reservation;

/// <summary>
///     Reservation state using discriminated union pattern.
///     Each nested record represents a distinct state in the reservation lifecycle.
/// </summary>
[JsonDerivedType(typeof(ReservationEmpty), nameof(ReservationEmpty))]
[JsonDerivedType(typeof(ReservationDraft), nameof(ReservationDraft))]
[JsonDerivedType(typeof(ReservationHeld), nameof(ReservationHeld))]
[JsonDerivedType(typeof(ReservationConfirmed), nameof(ReservationConfirmed))]
[JsonDerivedType(typeof(ReservationCancelled), nameof(ReservationCancelled))]
[JsonDerivedType(typeof(ReservationRejected), nameof(ReservationRejected))]
[JsonDerivedType(typeof(ReservationExpired), nameof(ReservationExpired))]
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
        string OrganizerName,
        DateTime StartTime,
        DateTime EndTime,
        string Purpose,
        List<string> SelectedEquipment) : ReservationState
    {
        public ReservationDraft() : this(Guid.Empty, Guid.Empty, Guid.Empty, string.Empty, DateTime.MinValue, DateTime.MinValue, string.Empty, []) { }
    }

    /// <summary>
    ///     Held state - reservation is committed but may need approval
    /// </summary>
    public record ReservationHeld(
        Guid ReservationId,
        Guid RoomId,
        Guid OrganizerId,
        string OrganizerName,
        DateTime StartTime,
        DateTime EndTime,
        string Purpose,
        List<string> SelectedEquipment,
        bool RequiresApproval,
        Guid? ApprovalRequestId,
        string? ApprovalRequestComment) : ReservationState
    {
        public ReservationHeld() : this(Guid.Empty, Guid.Empty, Guid.Empty, string.Empty, DateTime.MinValue, DateTime.MinValue, string.Empty, [], false, null, null) { }
    }

    /// <summary>
    ///     Confirmed state - reservation is final and active
    /// </summary>
    public record ReservationConfirmed(
        Guid ReservationId,
        Guid RoomId,
        Guid OrganizerId,
        string OrganizerName,
        DateTime StartTime,
        DateTime EndTime,
        string Purpose,
        List<string> SelectedEquipment,
        DateTime ConfirmedAt,
        Guid? ApprovalRequestId,
        string? ApprovalRequestComment,
        string? ApprovalDecisionComment) : ReservationState
    {
        public ReservationConfirmed() : this(Guid.Empty, Guid.Empty, Guid.Empty, string.Empty, DateTime.MinValue, DateTime.MinValue, string.Empty, [], DateTime.MinValue, null, null, null) { }
    }

    /// <summary>
    ///     Cancelled state - reservation was cancelled
    /// </summary>
    public record ReservationCancelled(
        Guid ReservationId,
        Guid RoomId,
        Guid OrganizerId,
        string OrganizerName,
        DateTime StartTime,
        DateTime EndTime,
        string Purpose,
        List<string> SelectedEquipment,
        string? ApprovalRequestComment,
        string Reason,
        DateTime CancelledAt) : ReservationState
    {
        public ReservationCancelled() : this(Guid.Empty, Guid.Empty, Guid.Empty, string.Empty, DateTime.MinValue, DateTime.MinValue, string.Empty, [], null, string.Empty, DateTime.MinValue) { }
    }

    /// <summary>
    ///     Rejected state - reservation was rejected during approval
    /// </summary>
    public record ReservationRejected(
        Guid ReservationId,
        Guid RoomId,
        Guid OrganizerId,
        string OrganizerName,
        DateTime StartTime,
        DateTime EndTime,
        string Purpose,
        List<string> SelectedEquipment,
        Guid ApprovalRequestId,
        string? ApprovalRequestComment,
        string Reason,
        DateTime RejectedAt) : ReservationState
    {
        public ReservationRejected() : this(Guid.Empty, Guid.Empty, Guid.Empty, string.Empty, DateTime.MinValue, DateTime.MinValue, string.Empty, [], Guid.Empty, null, string.Empty, DateTime.MinValue) { }
    }

    /// <summary>
    ///     Expired state - reservation hold or approval expired
    /// </summary>
    public record ReservationExpired(
        Guid ReservationId,
        Guid RoomId,
        string ExpiredReason,
        DateTime ExpiredAt) : ReservationState
    {
        public ReservationExpired() : this(Guid.Empty, Guid.Empty, string.Empty, DateTime.MinValue) { }
    }
}
