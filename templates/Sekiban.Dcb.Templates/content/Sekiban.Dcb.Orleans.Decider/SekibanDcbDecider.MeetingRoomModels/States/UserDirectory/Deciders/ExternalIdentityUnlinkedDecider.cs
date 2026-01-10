using Dcb.MeetingRoomModels.Events.UserDirectory;
namespace Dcb.MeetingRoomModels.States.UserDirectory.Deciders;

/// <summary>
///     Decider for ExternalIdentityUnlinked event
/// </summary>
public static class ExternalIdentityUnlinkedDecider
{
    /// <summary>
    ///     Apply ExternalIdentityUnlinked event to UserDirectoryState
    /// </summary>
    public static UserDirectoryState Evolve(this UserDirectoryState state, ExternalIdentityUnlinked unlinked) =>
        state switch
        {
            UserDirectoryState.UserDirectoryActive active => active with
            {
                ExternalIdentities = active.ExternalIdentities
                    .Where(e => !(e.Provider == unlinked.Provider && e.ExternalId == unlinked.ExternalId))
                    .ToList()
            },
            _ => state // Ignore if not in active state
        };
}
