using Dcb.MeetingRoomModels.Events.UserDirectory;
namespace Dcb.MeetingRoomModels.States.UserDirectory.Deciders;

/// <summary>
///     Decider for UserDeactivated event
/// </summary>
public static class UserDeactivatedDecider
{
    /// <summary>
    ///     Validate preconditions for deactivating a user
    /// </summary>
    public static void Validate(this UserDirectoryState state)
    {
        if (state is not UserDirectoryState.UserDirectoryActive)
        {
            throw new InvalidOperationException("Cannot deactivate non-active user");
        }
    }

    /// <summary>
    ///     Apply UserDeactivated event to UserDirectoryState
    /// </summary>
    public static UserDirectoryState Evolve(this UserDirectoryState state, UserDeactivated deactivated) =>
        state switch
        {
            UserDirectoryState.UserDirectoryActive active => new UserDirectoryState.UserDirectoryDeactivated(
                active.UserId,
                active.DisplayName,
                active.Email,
                active.Department,
                active.RegisteredAt,
                active.MonthlyReservationLimit,
                active.ExternalIdentities,
                deactivated.Reason,
                deactivated.DeactivatedAt),
            _ => state
        };
}
