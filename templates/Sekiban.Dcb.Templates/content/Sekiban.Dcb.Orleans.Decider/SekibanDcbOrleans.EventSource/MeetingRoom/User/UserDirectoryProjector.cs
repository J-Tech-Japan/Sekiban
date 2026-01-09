using Dcb.MeetingRoomModels.Events.UserDirectory;
using Dcb.MeetingRoomModels.States.UserDirectory;
using Dcb.MeetingRoomModels.States.UserDirectory.Deciders;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.EventSource.MeetingRoom.User;

public class UserDirectoryProjector : ITagProjector<UserDirectoryProjector>
{
    public static string ProjectorVersion => "1.0.0";
    public static string ProjectorName => nameof(UserDirectoryProjector);

    public static ITagStatePayload Project(ITagStatePayload current, Event ev)
    {
        var state = current as UserDirectoryState ?? UserDirectoryState.Empty;

        return ev.Payload switch
        {
            UserRegistered registered => state.Evolve(registered),
            UserProfileUpdated updated => state.Evolve(updated),
            UserDeactivated deactivated => state.Evolve(deactivated),
            UserReactivated reactivated => state.Evolve(reactivated),
            ExternalIdentityLinked linked => state.Evolve(linked),
            ExternalIdentityUnlinked unlinked => state.Evolve(unlinked),
            _ => state
        };
    }
}
