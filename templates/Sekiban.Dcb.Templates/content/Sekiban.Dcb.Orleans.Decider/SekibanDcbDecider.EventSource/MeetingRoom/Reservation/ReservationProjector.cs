using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.States.Reservation.Deciders;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.EventSource.MeetingRoom.Reservation;

public class ReservationProjector : ITagProjector<ReservationProjector>
{
    public static string ProjectorVersion => "1.0.0";
    public static string ProjectorName => nameof(ReservationProjector);

    public static ITagStatePayload Project(ITagStatePayload current, Event ev)
    {
        var state = current as ReservationState ?? ReservationState.Empty;

        return ev.Payload switch
        {
            ReservationDraftCreated created => state.Evolve(created),
            ReservationHoldCommitted committed => state.Evolve(committed),
            ReservationConfirmed confirmed => state.Evolve(confirmed),
            ReservationCancelled cancelled => state.Evolve(cancelled),
            ReservationRejected rejected => state.Evolve(rejected),
            ReservationDetailsUpdated updated => state.Evolve(updated),
            ReservationExpiredCommitted expired => state.Evolve(expired),
            _ => state
        };
    }
}
