using Dcb.MeetingRoomModels.Events.UserAccess;
using Dcb.MeetingRoomModels.States.UserAccess;
using Dcb.MeetingRoomModels.States.UserAccess.Deciders;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
namespace Dcb.EventSource.MeetingRoom.Projections;

/// <summary>
///     UserAccess list projection for multi-projection queries
/// </summary>
public record UserAccessListProjection : IMultiProjector<UserAccessListProjection>
{
    public Dictionary<Guid, UserAccessState> UserAccesses { get; init; } = [];

    public static string MultiProjectorName => nameof(UserAccessListProjection);
    public static string MultiProjectorVersion => "1.0.0";

    public static UserAccessListProjection GenerateInitialPayload() => new();

    public static UserAccessListProjection Project(
        UserAccessListProjection payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        var userAccessTags = tags.OfType<UserAccessTag>().ToList();
        if (userAccessTags.Count == 0) return payload;

        var updatedUserAccesses = new Dictionary<Guid, UserAccessState>(payload.UserAccesses);

        foreach (var tag in userAccessTags)
        {
            var userId = tag.UserId;
            var currentState = updatedUserAccesses.TryGetValue(userId, out var existing)
                ? existing
                : UserAccessState.Empty;

            var newState = ev.Payload switch
            {
                UserAccessGranted granted => currentState.Evolve(granted),
                UserRoleGranted roleGranted => currentState.Evolve(roleGranted),
                UserRoleRevoked roleRevoked => currentState.Evolve(roleRevoked),
                UserAccessDeactivated deactivated => currentState.Evolve(deactivated),
                UserAccessReactivated reactivated => currentState.Evolve(reactivated),
                _ => currentState
            };

            if (newState is not UserAccessState.UserAccessEmpty)
            {
                updatedUserAccesses[userId] = newState;
            }
        }

        return payload with { UserAccesses = updatedUserAccesses };
    }

    /// <summary>
    ///     Get all active user accesses
    /// </summary>
    public IReadOnlyList<UserAccessState.UserAccessActive> GetActiveUserAccesses() =>
        [.. UserAccesses.Values.OfType<UserAccessState.UserAccessActive>()
            .OrderByDescending(u => u.GrantedAt)];

    /// <summary>
    ///     Get all user accesses including deactivated
    /// </summary>
    public IReadOnlyList<UserAccessState> GetAllUserAccesses() =>
        [.. UserAccesses.Values
            .Where(u => u is not UserAccessState.UserAccessEmpty)];

    /// <summary>
    ///     Get user access by user ID
    /// </summary>
    public UserAccessState? GetUserAccess(Guid userId) =>
        UserAccesses.TryGetValue(userId, out var access) ? access : null;

    /// <summary>
    ///     Get users with a specific role
    /// </summary>
    public IReadOnlyList<UserAccessState.UserAccessActive> GetUsersByRole(string role) =>
        [.. UserAccesses.Values.OfType<UserAccessState.UserAccessActive>()
            .Where(u => u.HasRole(role))
            .OrderByDescending(u => u.GrantedAt)];

    /// <summary>
    ///     Check if user has a specific role
    /// </summary>
    public bool UserHasRole(Guid userId, string role) =>
        UserAccesses.TryGetValue(userId, out var access)
        && access is UserAccessState.UserAccessActive active
        && active.HasRole(role);
}
