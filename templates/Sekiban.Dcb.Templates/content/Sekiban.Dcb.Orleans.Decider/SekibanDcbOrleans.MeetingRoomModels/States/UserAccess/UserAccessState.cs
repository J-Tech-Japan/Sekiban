using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.States.UserAccess;

/// <summary>
///     Represents a user's access permissions and roles
/// </summary>
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
        DateTime DeactivatedAt) : UserAccessState;
}
