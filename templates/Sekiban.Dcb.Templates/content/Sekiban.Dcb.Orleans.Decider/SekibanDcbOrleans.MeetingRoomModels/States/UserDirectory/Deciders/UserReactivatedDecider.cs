using Dcb.MeetingRoomModels.Events.UserDirectory;
namespace Dcb.MeetingRoomModels.States.UserDirectory.Deciders;

/// <summary>
///     Decider for UserReactivated event
/// </summary>
public static class UserReactivatedDecider
{
    /// <summary>
    ///     Validate preconditions for reactivating a user
    /// </summary>
    public static void Validate(this UserDirectoryState state)
    {
        if (state is not UserDirectoryState.UserDirectoryDeactivated)
        {
            throw new InvalidOperationException("Cannot reactivate non-deactivated user");
        }
    }

    /// <summary>
    ///     Apply UserReactivated event to UserDirectoryState
    /// </summary>
    public static UserDirectoryState Evolve(this UserDirectoryState state, UserReactivated reactivated) =>
        state switch
        {
            UserDirectoryState.UserDirectoryDeactivated deactivated => new UserDirectoryState.UserDirectoryActive(
                deactivated.UserId,
                deactivated.DisplayName,
                deactivated.Email,
                deactivated.Department,
                deactivated.RegisteredAt),
            _ => state
        };
}
