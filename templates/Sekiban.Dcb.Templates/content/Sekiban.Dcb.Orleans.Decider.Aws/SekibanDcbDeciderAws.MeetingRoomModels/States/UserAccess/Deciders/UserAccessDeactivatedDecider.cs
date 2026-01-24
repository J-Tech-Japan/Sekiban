using Dcb.MeetingRoomModels.Events.UserAccess;
namespace Dcb.MeetingRoomModels.States.UserAccess.Deciders;

/// <summary>
///     Decider for UserAccessDeactivated event
/// </summary>
public static class UserAccessDeactivatedDecider
{
    /// <summary>
    ///     Validate preconditions for deactivating access
    /// </summary>
    public static void Validate(this UserAccessState state)
    {
        if (state is not UserAccessState.UserAccessActive)
        {
            throw new InvalidOperationException("Cannot deactivate non-active user access");
        }
    }

    /// <summary>
    ///     Apply UserAccessDeactivated event to UserAccessState
    /// </summary>
    public static UserAccessState Evolve(this UserAccessState state, UserAccessDeactivated deactivated) =>
        state switch
        {
            UserAccessState.UserAccessActive active => new UserAccessState.UserAccessDeactivated(
                active.UserId,
                active.Roles,
                active.GrantedAt,
                deactivated.Reason,
                deactivated.DeactivatedAt),
            _ => state
        };
}
