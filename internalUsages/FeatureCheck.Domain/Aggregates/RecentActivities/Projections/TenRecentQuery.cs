using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
namespace FeatureCheck.Domain.Aggregates.RecentActivities.Projections;

public record TenRecentQuery : ISingleProjectionListQuery<TenRecentProjection, TenRecentQuery.Parameter,
    TenRecentQuery.Responsse>
{
    public IEnumerable<Responsse> HandleFilter(Parameter queryParam,
        IEnumerable<SingleProjectionState<TenRecentProjection>> list)
    {
        return list.Select(
            m => new Responsse(
                m.Payload,
                m.AggregateId,
                m.LastEventId,
                m.LastSortableUniqueId,
                m.AppliedSnapshotVersion,
                m.Version,
                m.RootPartitionKey));
    }

    public IEnumerable<Responsse> HandleSort(Parameter queryParam, IEnumerable<Responsse> filteredList)
    {
        return filteredList.OrderByDescending(m => m.LastSortableUniqueId);
    }

    public record Parameter(int? PageSize, int? PageNumber) : IListQueryPagingParameter<Responsse>;

    public record Responsse(
        TenRecentProjection Payload,
        Guid AggregateId,
        Guid LastEventId,
        string LastSortableUniqueId,
        int AppliedSnapshotVersion,
        int Version,
        string RootPartitionKey) : SingleProjectionState<TenRecentProjection>(
        Payload,
        AggregateId,
        LastEventId,
        LastSortableUniqueId,
        AppliedSnapshotVersion,
        Version,
        RootPartitionKey), IQueryResponse;
}
