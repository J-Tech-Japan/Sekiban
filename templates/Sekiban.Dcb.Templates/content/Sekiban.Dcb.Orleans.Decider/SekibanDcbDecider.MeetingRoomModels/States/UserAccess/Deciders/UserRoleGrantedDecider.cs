using Dcb.MeetingRoomModels.Events.UserAccess;
namespace Dcb.MeetingRoomModels.States.UserAccess.Deciders;

/// <summary>
///     Decider for UserRoleGranted event
/// </summary>
public static class UserRoleGrantedDecider
{
    /// <summary>
    ///     Validate preconditions for granting a role
    /// </summary>
    public static void Validate(this UserAccessState state, string role)
    {
        if (state is not UserAccessState.UserAccessActive active)
        {
            throw new InvalidOperationException("Cannot grant role to non-active user access");
        }

        if (active.HasRole(role))
        {
            throw new InvalidOperationException($"User already has role: {role}");
        }
    }

    /// <summary>
    ///     Apply UserRoleGranted event to UserAccessState
    /// </summary>
    public static UserAccessState Evolve(this UserAccessState state, UserRoleGranted granted) =>
        state switch
        {
            UserAccessState.UserAccessActive active when !active.HasRole(granted.Role) =>
                active with { Roles = [..active.Roles, granted.Role] },
            _ => state
        };
}
