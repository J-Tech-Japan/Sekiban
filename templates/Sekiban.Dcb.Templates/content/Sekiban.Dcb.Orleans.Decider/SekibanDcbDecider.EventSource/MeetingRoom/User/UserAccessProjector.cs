using Dcb.MeetingRoomModels.Events.UserAccess;
using Dcb.MeetingRoomModels.States.UserAccess;
using Dcb.MeetingRoomModels.States.UserAccess.Deciders;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.EventSource.MeetingRoom.User;

public class UserAccessProjector : ITagProjector<UserAccessProjector>
{
    public static string ProjectorVersion => "1.0.0";
    public static string ProjectorName => nameof(UserAccessProjector);

    public static ITagStatePayload Project(ITagStatePayload current, Event ev)
    {
        var state = current as UserAccessState ?? UserAccessState.Empty;

        return ev.Payload switch
        {
            UserAccessGranted granted => state.Evolve(granted),
            UserRoleGranted roleGranted => state.Evolve(roleGranted),
            UserRoleRevoked roleRevoked => state.Evolve(roleRevoked),
            UserAccessDeactivated deactivated => state.Evolve(deactivated),
            UserAccessReactivated reactivated => state.Evolve(reactivated),
            _ => state
        };
    }
}
