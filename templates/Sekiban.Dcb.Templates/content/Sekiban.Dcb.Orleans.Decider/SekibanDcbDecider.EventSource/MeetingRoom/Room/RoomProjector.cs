using Dcb.MeetingRoomModels.Events.Room;
using Dcb.MeetingRoomModels.States.Room;
using Dcb.MeetingRoomModels.States.Room.Deciders;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.EventSource.MeetingRoom.Room;

public class RoomProjector : ITagProjector<RoomProjector>
{
    public static string ProjectorVersion => "1.0.0";
    public static string ProjectorName => nameof(RoomProjector);

    public static ITagStatePayload Project(ITagStatePayload current, Event ev)
    {
        var state = current as RoomState ?? RoomState.Empty;

        return ev.Payload switch
        {
            RoomCreated created => RoomCreatedDecider.Create(created),
            RoomUpdated updated => RoomUpdatedDecider.Evolve(state, updated),
            RoomDeactivated deactivated => RoomDeactivatedDecider.Evolve(state, deactivated),
            RoomReactivated reactivated => RoomReactivatedDecider.Evolve(state, reactivated),
            _ => state
        };
    }
}
