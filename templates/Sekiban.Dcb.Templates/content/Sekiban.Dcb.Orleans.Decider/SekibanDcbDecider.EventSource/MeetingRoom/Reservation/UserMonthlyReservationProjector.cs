using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.UserMonthlyReservation;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.EventSource.MeetingRoom.Reservation;

public class UserMonthlyReservationProjector : ITagProjector<UserMonthlyReservationProjector>
{
    public static string ProjectorVersion => "1.0.0";
    public static string ProjectorName => nameof(UserMonthlyReservationProjector);

    public static ITagStatePayload Project(ITagStatePayload current, Event ev)
    {
        var state = current as UserMonthlyReservationState ?? UserMonthlyReservationState.Empty;

        return ev.Payload switch
        {
            ReservationDraftCreated created => state.RegisterRequest(
                created.OrganizerId,
                new DateOnly(created.StartTime.Year, created.StartTime.Month, 1)),
            ReservationRejected => state.RegisterRejection(),
            _ => state
        };
    }
}
