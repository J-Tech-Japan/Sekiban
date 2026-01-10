using Dcb.MeetingRoomModels.Events.UserDirectory;
namespace Dcb.MeetingRoomModels.States.UserDirectory.Deciders;

/// <summary>
///     Decider for UserRegistered event
/// </summary>
public static class UserRegisteredDecider
{
    /// <summary>
    ///     Validate preconditions for registering a user
    /// </summary>
    public static void Validate(this UserDirectoryState state)
    {
        if (state is not UserDirectoryState.UserDirectoryEmpty)
        {
            throw new InvalidOperationException("User is already registered");
        }
    }

    /// <summary>
    ///     Create a new UserDirectoryState from UserRegistered event
    /// </summary>
    public static UserDirectoryState.UserDirectoryActive Create(UserRegistered registered) =>
        new(
            registered.UserId,
            registered.DisplayName,
            registered.Email,
            registered.Department,
            registered.RegisteredAt,
            registered.MonthlyReservationLimit,
            []);

    /// <summary>
    ///     Apply UserRegistered event to UserDirectoryState
    /// </summary>
    public static UserDirectoryState Evolve(this UserDirectoryState state, UserRegistered registered) =>
        Create(registered);
}
