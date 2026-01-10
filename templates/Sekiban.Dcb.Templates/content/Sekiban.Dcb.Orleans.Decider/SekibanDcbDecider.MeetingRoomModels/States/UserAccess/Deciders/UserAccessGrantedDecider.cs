using Dcb.MeetingRoomModels.Events.UserAccess;
namespace Dcb.MeetingRoomModels.States.UserAccess.Deciders;

/// <summary>
///     Decider for UserAccessGranted event
/// </summary>
public static class UserAccessGrantedDecider
{
    /// <summary>
    ///     Validate preconditions for granting access
    /// </summary>
    public static void Validate(this UserAccessState state)
    {
        if (state is not UserAccessState.UserAccessEmpty)
        {
            throw new InvalidOperationException("User already has access granted");
        }
    }

    /// <summary>
    ///     Create a new UserAccessState from UserAccessGranted event
    /// </summary>
    public static UserAccessState.UserAccessActive Create(UserAccessGranted granted) =>
        new(
            granted.UserId,
            [granted.InitialRole],
            granted.GrantedAt);

    /// <summary>
    ///     Apply UserAccessGranted event to UserAccessState
    /// </summary>
    public static UserAccessState Evolve(this UserAccessState state, UserAccessGranted granted) =>
        Create(granted);
}
