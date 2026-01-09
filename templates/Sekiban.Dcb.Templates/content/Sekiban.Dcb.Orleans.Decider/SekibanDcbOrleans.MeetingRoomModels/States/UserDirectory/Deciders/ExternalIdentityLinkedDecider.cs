using Dcb.MeetingRoomModels.Events.UserDirectory;
namespace Dcb.MeetingRoomModels.States.UserDirectory.Deciders;

/// <summary>
///     Decider for ExternalIdentityLinked event
/// </summary>
public static class ExternalIdentityLinkedDecider
{
    /// <summary>
    ///     Apply ExternalIdentityLinked event to UserDirectoryState
    /// </summary>
    public static UserDirectoryState Evolve(this UserDirectoryState state, ExternalIdentityLinked linked) =>
        state switch
        {
            UserDirectoryState.UserDirectoryActive active => active with
            {
                ExternalIdentities = [..active.ExternalIdentities, new ExternalIdentity(linked.Provider, linked.ExternalId, linked.LinkedAt)]
            },
            _ => state // Ignore if not in active state
        };
}
