using Dcb.MeetingRoomModels.States.UserDirectory;
using Dcb.MeetingRoomModels.Tags;
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
    List<string> ExternalProviders);

[GenerateSerializer]
public record GetUserDirectoryListQuery :
    IMultiProjectionListQuery<GenericTagMultiProjector<UserDirectoryProjector, UserTag>, GetUserDirectoryListQuery, UserDirectoryListItem>,
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
        GenericTagMultiProjector<UserDirectoryProjector, UserTag> projector,
        GetUserDirectoryListQuery query,
        IQueryContext context)
    {
        var states = projector.GetStatePayloads().OfType<UserDirectoryState>();

        return states.Select(s => s switch
        {
            UserDirectoryState.UserDirectoryActive active => new UserDirectoryListItem(
                active.UserId,
                active.DisplayName,
                active.Email,
                active.Department,
                true,
                active.RegisteredAt,
                active.ExternalIdentities.Select(e => e.Provider).ToList()),
            UserDirectoryState.UserDirectoryDeactivated deactivated => new UserDirectoryListItem(
                deactivated.UserId,
                deactivated.DisplayName,
                deactivated.Email,
                deactivated.Department,
                false,
                deactivated.RegisteredAt,
                deactivated.ExternalIdentities.Select(e => e.Provider).ToList()),
            _ => null
        })
        .Where(x => x != null)
        .Cast<UserDirectoryListItem>()
        .Where(x => !query.ActiveOnly || x.IsActive);
    }

    public static IEnumerable<UserDirectoryListItem> HandleSort(
        IEnumerable<UserDirectoryListItem> filteredList,
        GetUserDirectoryListQuery query,
        IQueryContext context) =>
        filteredList.OrderBy(s => s.DisplayName, StringComparer.Ordinal);
}
