using System.Text.Json.Serialization;
using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.States.UserDirectory;

/// <summary>
///     Represents a user's directory information (profile data)
/// </summary>
[JsonDerivedType(typeof(UserDirectoryEmpty), nameof(UserDirectoryEmpty))]
[JsonDerivedType(typeof(UserDirectoryActive), nameof(UserDirectoryActive))]
[JsonDerivedType(typeof(UserDirectoryDeactivated), nameof(UserDirectoryDeactivated))]
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
        // Parameterless constructor for JSON deserialization
        public UserDirectoryActive() : this(Guid.Empty, string.Empty, string.Empty, null, DateTime.MinValue, DefaultMonthlyReservationLimit, []) { }

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
        // Parameterless constructor for JSON deserialization
        public UserDirectoryDeactivated() : this(Guid.Empty, string.Empty, string.Empty, null, DateTime.MinValue, DefaultMonthlyReservationLimit, [], null, DateTime.MinValue) { }

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
    DateTime LinkedAt)
{
    // Parameterless constructor for JSON deserialization
    public ExternalIdentity() : this(string.Empty, string.Empty, DateTime.MinValue) { }
}
