using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.States.UserDirectory;

/// <summary>
///     Represents a user's directory information (profile data)
/// </summary>
public abstract record UserDirectoryState : ITagStatePayload
{
    public const int DefaultMonthlyReservationLimit = 5;

    public static UserDirectoryState Empty => new UserDirectoryEmpty();

    /// <summary>
    ///     Empty state - user not yet registered
    /// </summary>
    public record UserDirectoryEmpty() : UserDirectoryState;

    /// <summary>
    ///     Active user state
    /// </summary>
    public record UserDirectoryActive(
        Guid UserId,
        string DisplayName,
        string Email,
        string? Department,
        DateTime RegisteredAt,
        int MonthlyReservationLimit,
        List<ExternalIdentity> ExternalIdentities) : UserDirectoryState
    {
        public UserDirectoryActive(
            Guid UserId,
            string DisplayName,
            string Email,
            string? Department,
            DateTime RegisteredAt)
            : this(UserId, DisplayName, Email, Department, RegisteredAt, DefaultMonthlyReservationLimit, []) { }
    }

    /// <summary>
    ///     Deactivated user state
    /// </summary>
    public record UserDirectoryDeactivated(
        Guid UserId,
        string DisplayName,
        string Email,
        string? Department,
        DateTime RegisteredAt,
        int MonthlyReservationLimit,
        List<ExternalIdentity> ExternalIdentities,
        string? DeactivationReason,
        DateTime DeactivatedAt) : UserDirectoryState
    {
        public UserDirectoryDeactivated(
            Guid UserId,
            string DisplayName,
            string Email,
            string? Department,
            DateTime RegisteredAt,
            string? DeactivationReason,
            DateTime DeactivatedAt)
            : this(UserId, DisplayName, Email, Department, RegisteredAt, DefaultMonthlyReservationLimit, [], DeactivationReason, DeactivatedAt) { }
    }
}

/// <summary>
///     Represents an external identity link (e.g., SSO provider)
/// </summary>
public record ExternalIdentity(
    string Provider,
    string ExternalId,
    DateTime LinkedAt);
