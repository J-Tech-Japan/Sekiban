using Dcb.MeetingRoomModels.States.UserAccess;
using Dcb.MeetingRoomModels.Tags;
using Dcb.EventSource.MeetingRoom.User;
using Orleans;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
namespace Dcb.EventSource.MeetingRoom.Queries;

public record UserAccessListItem(
    Guid UserId,
    List<string> Roles,
    bool IsActive,
    DateTime GrantedAt);

[GenerateSerializer]
public record GetUserAccessListQuery :
    IMultiProjectionListQuery<GenericTagMultiProjector<UserAccessProjector, UserAccessTag>, GetUserAccessListQuery, UserAccessListItem>,
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

    [Id(4)]
    public string? RoleFilter { get; init; }

    public static IEnumerable<UserAccessListItem> HandleFilter(
        GenericTagMultiProjector<UserAccessProjector, UserAccessTag> projector,
        GetUserAccessListQuery query,
        IQueryContext context)
    {
        var states = projector.GetStatePayloads().OfType<UserAccessState>();

        return states.Select(s => s switch
        {
            UserAccessState.UserAccessActive active => new UserAccessListItem(
                active.UserId,
                active.Roles,
                true,
                active.GrantedAt),
            UserAccessState.UserAccessDeactivated deactivated => new UserAccessListItem(
                deactivated.UserId,
                deactivated.Roles,
                false,
                deactivated.GrantedAt),
            _ => null
        })
        .Where(x => x != null)
        .Cast<UserAccessListItem>()
        .Where(x => !query.ActiveOnly || x.IsActive)
        .Where(x => string.IsNullOrEmpty(query.RoleFilter) || x.Roles.Contains(query.RoleFilter));
    }

    public static IEnumerable<UserAccessListItem> HandleSort(
        IEnumerable<UserAccessListItem> filteredList,
        GetUserAccessListQuery query,
        IQueryContext context) =>
        filteredList.OrderByDescending(s => s.GrantedAt);
}
