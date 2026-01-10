using Dcb.MeetingRoomModels.Events.UserAccess;
namespace Dcb.MeetingRoomModels.States.UserAccess.Deciders;

/// <summary>
///     Decider for UserRoleRevoked event
/// </summary>
public static class UserRoleRevokedDecider
{
    /// <summary>
    ///     Validate preconditions for revoking a role
    /// </summary>
    public static void Validate(this UserAccessState state, string role)
    {
        if (state is not UserAccessState.UserAccessActive active)
        {
            throw new InvalidOperationException("Cannot revoke role from non-active user access");
        }

        if (!active.HasRole(role))
        {
            throw new InvalidOperationException($"User does not have role: {role}");
        }
    }

    /// <summary>
    ///     Apply UserRoleRevoked event to UserAccessState
    /// </summary>
    public static UserAccessState Evolve(this UserAccessState state, UserRoleRevoked revoked) =>
        state switch
        {
            UserAccessState.UserAccessActive active when active.HasRole(revoked.Role) =>
                active with { Roles = active.Roles.Where(r => r != revoked.Role).ToList() },
            _ => state
        };
}
