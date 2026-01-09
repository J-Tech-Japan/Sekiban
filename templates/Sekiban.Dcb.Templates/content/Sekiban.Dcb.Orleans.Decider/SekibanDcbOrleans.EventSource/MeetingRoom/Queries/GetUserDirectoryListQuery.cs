using Dcb.MeetingRoomModels.States.UserDirectory;
using Dcb.EventSource.MeetingRoom.Projections;
using Dcb.EventSource.MeetingRoom.User;
using Orleans;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
namespace Dcb.EventSource.MeetingRoom.Queries;

public record UserDirectoryListItem(
    Guid UserId,
    string DisplayName,
    string Email,
    string? Department,
    bool IsActive,
    DateTime RegisteredAt,
    int MonthlyReservationLimit,
    List<string> ExternalProviders,
    List<string> Roles)
{
    /// <summary>
    ///     Creates a new instance with roles
    /// </summary>
    public UserDirectoryListItem WithRoles(List<string> roles) =>
        this with { Roles = roles };
}

[GenerateSerializer]
public record GetUserDirectoryListQuery :
    IMultiProjectionListQuery<UserDirectoryListProjection, GetUserDirectoryListQuery, UserDirectoryListItem>,
    IWaitForSortableUniqueId,
    IQueryPagingParameter
{
    [Id(0)]
    public int? PageNumber { get; init; }

    [Id(1)]
    public int? PageSize { get; init; }

    [Id(2)]
    public string? WaitForSortableUniqueId { get; init; }

    [Id(3)]
    public bool ActiveOnly { get; init; } = false;

    public static IEnumerable<UserDirectoryListItem> HandleFilter(
        UserDirectoryListProjection projector,
        GetUserDirectoryListQuery query,
        IQueryContext context)
    {
        var states = query.ActiveOnly
            ? projector.GetActiveUsers().Cast<UserDirectoryState>()
            : projector.GetAllUsers().Cast<UserDirectoryState>();

        return states.Select(s => s switch
        {
            UserDirectoryState.UserDirectoryActive active => new UserDirectoryListItem(
                active.UserId,
                active.DisplayName,
                active.Email,
                active.Department,
                true,
                active.RegisteredAt,
                active.MonthlyReservationLimit,
                active.ExternalIdentities.Select(e => e.Provider).ToList(),
                []),  // Roles will be populated by endpoint
            UserDirectoryState.UserDirectoryDeactivated deactivated => new UserDirectoryListItem(
                deactivated.UserId,
                deactivated.DisplayName,
                deactivated.Email,
                deactivated.Department,
                false,
                deactivated.RegisteredAt,
                deactivated.MonthlyReservationLimit,
                deactivated.ExternalIdentities.Select(e => e.Provider).ToList(),
                []),  // Roles will be populated by endpoint
            _ => null
        })
        .Where(x => x != null)
        .Cast<UserDirectoryListItem>();
    }

    public static IEnumerable<UserDirectoryListItem> HandleSort(
        IEnumerable<UserDirectoryListItem> filteredList,
        GetUserDirectoryListQuery query,
        IQueryContext context) =>
        filteredList.OrderBy(s => s.DisplayName, StringComparer.Ordinal);
}
