using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.States.Reservation.Deciders;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
namespace Dcb.EventSource.MeetingRoom.Projections;

/// <summary>
///     Reservation list projection for multi-projection queries
/// </summary>
public record ReservationListProjection : IMultiProjector<ReservationListProjection>
{
    public Dictionary<Guid, ReservationState> Reservations { get; init; } = [];

    public static string MultiProjectorName => nameof(ReservationListProjection);
    public static string MultiProjectorVersion => "1.0.0";

    public static ReservationListProjection GenerateInitialPayload() => new();

    public static ReservationListProjection Project(
        ReservationListProjection payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        var reservationTags = tags.OfType<ReservationTag>().ToList();
        if (reservationTags.Count == 0) return payload;

        var updatedReservations = new Dictionary<Guid, ReservationState>(payload.Reservations);

        foreach (var tag in reservationTags)
        {
            var reservationId = tag.ReservationId;
            var currentState = updatedReservations.TryGetValue(reservationId, out var existing)
                ? existing
                : ReservationState.Empty;

            var newState = ev.Payload switch
            {
                ReservationDraftCreated created => currentState.Evolve(created),
                ReservationHoldCommitted committed => currentState.Evolve(committed),
                ReservationConfirmed confirmed => currentState.Evolve(confirmed),
                ReservationCancelled cancelled => currentState.Evolve(cancelled),
                ReservationRejected rejected => currentState.Evolve(rejected),
                ReservationDetailsUpdated updated => currentState.Evolve(updated),
                ReservationExpiredCommitted expired => currentState.Evolve(expired),
                _ => currentState
            };

            if (newState is not ReservationState.ReservationEmpty)
            {
                updatedReservations[reservationId] = newState;
            }
        }

        return payload with { Reservations = updatedReservations };
    }

    /// <summary>
    ///     Get all active reservations (Draft, Held, Confirmed)
    /// </summary>
    public IReadOnlyList<ReservationState> GetActiveReservations() =>
        [.. Reservations.Values
            .Where(r => r is ReservationState.ReservationDraft
                or ReservationState.ReservationHeld
                or ReservationState.ReservationConfirmed)
            .OrderBy(r => GetStartTime(r))];

    /// <summary>
    ///     Get all confirmed reservations
    /// </summary>
    public IReadOnlyList<ReservationState.ReservationConfirmed> GetConfirmedReservations() =>
        [.. Reservations.Values.OfType<ReservationState.ReservationConfirmed>()
            .OrderBy(r => r.StartTime)];

    /// <summary>
    ///     Get all reservations
    /// </summary>
    public IReadOnlyList<ReservationState> GetAllReservations() =>
        [.. Reservations.Values];

    /// <summary>
    ///     Get reservation by ID
    /// </summary>
    public ReservationState? GetReservation(Guid reservationId) =>
        Reservations.TryGetValue(reservationId, out var reservation) ? reservation : null;

    /// <summary>
    ///     Get reservations by room ID
    /// </summary>
    public IReadOnlyList<ReservationState> GetReservationsByRoom(Guid roomId) =>
        [.. Reservations.Values.Where(r => GetRoomId(r) == roomId)];

    private static DateTime? GetStartTime(ReservationState state) => state switch
    {
        ReservationState.ReservationDraft draft => draft.StartTime,
        ReservationState.ReservationHeld held => held.StartTime,
        ReservationState.ReservationConfirmed confirmed => confirmed.StartTime,
        _ => null
    };

    private static Guid? GetRoomId(ReservationState state) => state switch
    {
        ReservationState.ReservationDraft draft => draft.RoomId,
        ReservationState.ReservationHeld held => held.RoomId,
        ReservationState.ReservationConfirmed confirmed => confirmed.RoomId,
        ReservationState.ReservationCancelled cancelled => cancelled.RoomId,
        ReservationState.ReservationRejected rejected => rejected.RoomId,
        ReservationState.ReservationExpired expired => expired.RoomId,
        _ => null
    };
}
