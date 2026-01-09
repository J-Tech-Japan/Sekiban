using Dcb.MeetingRoomModels.Events.UserDirectory;
namespace Dcb.MeetingRoomModels.States.UserDirectory.Deciders;

/// <summary>
///     Decider for UserProfileUpdated event
/// </summary>
public static class UserProfileUpdatedDecider
{
    /// <summary>
    ///     Validate preconditions for updating user profile
    /// </summary>
    public static void Validate(this UserDirectoryState state)
    {
        if (state is not UserDirectoryState.UserDirectoryActive)
        {
            throw new InvalidOperationException("Cannot update profile for non-active user");
        }
    }

    /// <summary>
    ///     Apply UserProfileUpdated event to UserDirectoryState
    /// </summary>
    public static UserDirectoryState Evolve(this UserDirectoryState state, UserProfileUpdated updated) =>
        state switch
        {
            UserDirectoryState.UserDirectoryActive active => active with
            {
                DisplayName = updated.DisplayName,
                Email = updated.Email,
                Department = updated.Department,
                MonthlyReservationLimit = updated.MonthlyReservationLimit
            },
            _ => state
        };
}
