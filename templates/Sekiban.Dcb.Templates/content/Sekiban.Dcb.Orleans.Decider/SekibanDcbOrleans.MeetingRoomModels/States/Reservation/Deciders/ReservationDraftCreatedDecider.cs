using Dcb.MeetingRoomModels.Events.Reservation;
namespace Dcb.MeetingRoomModels.States.Reservation.Deciders;

/// <summary>
///     Decider for ReservationDraftCreated event
/// </summary>
public static class ReservationDraftCreatedDecider
{
    /// <summary>
    ///     Create a new ReservationDraft from ReservationDraftCreated event
    /// </summary>
    public static ReservationState.ReservationDraft Create(ReservationDraftCreated created) =>
        new(
            created.ReservationId,
            created.RoomId,
            created.OrganizerId,
            created.OrganizerName,
            created.StartTime,
            created.EndTime,
            created.Purpose);

    /// <summary>
    ///     Apply ReservationDraftCreated event to ReservationState
    /// </summary>
    public static ReservationState Evolve(this ReservationState state, ReservationDraftCreated created) =>
        state switch
        {
            ReservationState.ReservationEmpty => Create(created),
            _ => state // Idempotency: ignore if already created
        };
}
