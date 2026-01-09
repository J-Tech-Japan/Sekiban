using Dcb.MeetingRoomModels.Events.UserDirectory;
using Dcb.MeetingRoomModels.States.UserDirectory;
using Dcb.MeetingRoomModels.States.UserDirectory.Deciders;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
namespace Dcb.EventSource.MeetingRoom.Projections;

/// <summary>
///     UserDirectory list projection for multi-projection queries
/// </summary>
public record UserDirectoryListProjection : IMultiProjector<UserDirectoryListProjection>
{
    public Dictionary<Guid, UserDirectoryState> Users { get; init; } = [];

    public static string MultiProjectorName => nameof(UserDirectoryListProjection);
    public static string MultiProjectorVersion => "1.0.0";

    public static UserDirectoryListProjection GenerateInitialPayload() => new();

    public static UserDirectoryListProjection Project(
        UserDirectoryListProjection payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        var userTags = tags.OfType<UserTag>().ToList();
        if (userTags.Count == 0) return payload;

        var updatedUsers = new Dictionary<Guid, UserDirectoryState>(payload.Users);

        foreach (var tag in userTags)
        {
            var userId = tag.UserId;
            var currentState = updatedUsers.TryGetValue(userId, out var existing)
                ? existing
                : UserDirectoryState.Empty;

            var newState = ev.Payload switch
            {
                UserRegistered registered => currentState.Evolve(registered),
                UserProfileUpdated updated => currentState.Evolve(updated),
                UserDeactivated deactivated => currentState.Evolve(deactivated),
                UserReactivated reactivated => currentState.Evolve(reactivated),
                ExternalIdentityLinked linked => currentState.Evolve(linked),
                ExternalIdentityUnlinked unlinked => currentState.Evolve(unlinked),
                _ => currentState
            };

            if (newState is not UserDirectoryState.UserDirectoryEmpty)
            {
                updatedUsers[userId] = newState;
            }
        }

        return payload with { Users = updatedUsers };
    }

    /// <summary>
    ///     Get all active users
    /// </summary>
    public IReadOnlyList<UserDirectoryState.UserDirectoryActive> GetActiveUsers() =>
        [.. Users.Values.OfType<UserDirectoryState.UserDirectoryActive>()
            .OrderBy(u => u.DisplayName, StringComparer.Ordinal)];

    /// <summary>
    ///     Get all users including deactivated
    /// </summary>
    public IReadOnlyList<UserDirectoryState> GetAllUsers() =>
        [.. Users.Values
            .Where(u => u is not UserDirectoryState.UserDirectoryEmpty)];

    /// <summary>
    ///     Get user by ID
    /// </summary>
    public UserDirectoryState? GetUser(Guid userId) =>
        Users.TryGetValue(userId, out var user) ? user : null;

    /// <summary>
    ///     Get users by email domain
    /// </summary>
    public IReadOnlyList<UserDirectoryState.UserDirectoryActive> GetUsersByEmailDomain(string domain) =>
        [.. Users.Values.OfType<UserDirectoryState.UserDirectoryActive>()
            .Where(u => u.Email.EndsWith($"@{domain}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(u => u.DisplayName, StringComparer.Ordinal)];

    /// <summary>
    ///     Get users by department
    /// </summary>
    public IReadOnlyList<UserDirectoryState.UserDirectoryActive> GetUsersByDepartment(string department) =>
        [.. Users.Values.OfType<UserDirectoryState.UserDirectoryActive>()
            .Where(u => u.Department == department)
            .OrderBy(u => u.DisplayName, StringComparer.Ordinal)];
}
