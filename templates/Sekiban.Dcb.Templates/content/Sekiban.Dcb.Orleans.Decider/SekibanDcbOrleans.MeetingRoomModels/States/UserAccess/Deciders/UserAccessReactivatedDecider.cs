using Dcb.MeetingRoomModels.Events.UserAccess;
namespace Dcb.MeetingRoomModels.States.UserAccess.Deciders;

/// <summary>
///     Decider for UserAccessReactivated event
/// </summary>
public static class UserAccessReactivatedDecider
{
    /// <summary>
    ///     Validate preconditions for reactivating access
    /// </summary>
    public static void Validate(this UserAccessState state)
    {
        if (state is not UserAccessState.UserAccessDeactivated)
        {
            throw new InvalidOperationException("Cannot reactivate non-deactivated user access");
        }
    }

    /// <summary>
    ///     Apply UserAccessReactivated event to UserAccessState
    /// </summary>
    public static UserAccessState Evolve(this UserAccessState state, UserAccessReactivated reactivated) =>
        state switch
        {
            UserAccessState.UserAccessDeactivated deactivated => new UserAccessState.UserAccessActive(
                deactivated.UserId,
                deactivated.Roles,
                deactivated.GrantedAt),
            _ => state
        };
}
