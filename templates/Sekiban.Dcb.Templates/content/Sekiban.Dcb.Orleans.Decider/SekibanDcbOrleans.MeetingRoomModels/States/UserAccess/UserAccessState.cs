using System.Text.Json.Serialization;
using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.States.UserAccess;

/// <summary>
///     Represents a user's access permissions and roles
/// </summary>
[JsonDerivedType(typeof(UserAccessEmpty), nameof(UserAccessEmpty))]
[JsonDerivedType(typeof(UserAccessActive), nameof(UserAccessActive))]
[JsonDerivedType(typeof(UserAccessDeactivated), nameof(UserAccessDeactivated))]
public abstract record UserAccessState : ITagStatePayload
{
    public static UserAccessState Empty => new UserAccessEmpty();

    /// <summary>
    ///     Empty state - no access granted yet
    /// </summary>
    public record UserAccessEmpty() : UserAccessState;

    /// <summary>
    ///     Active access state with roles
    /// </summary>
    public record UserAccessActive(
        Guid UserId,
        List<string> Roles,
        DateTime GrantedAt) : UserAccessState
    {
        // Parameterless constructor for JSON deserialization
        public UserAccessActive() : this(Guid.Empty, [], DateTime.MinValue) { }

        public bool HasRole(string role) => Roles.Contains(role);
    }

    /// <summary>
    ///     Deactivated access state
    /// </summary>
    public record UserAccessDeactivated(
        Guid UserId,
        List<string> Roles,
        DateTime GrantedAt,
        string? DeactivationReason,
        DateTime DeactivatedAt) : UserAccessState
    {
        // Parameterless constructor for JSON deserialization
        public UserAccessDeactivated() : this(Guid.Empty, [], DateTime.MinValue, null, DateTime.MinValue) { }
    }
}
