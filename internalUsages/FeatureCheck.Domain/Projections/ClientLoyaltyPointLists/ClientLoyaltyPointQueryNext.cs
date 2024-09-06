using ResultBoxes;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;

public record ClientLoyaltyPointQueryNext(
    Guid? BranchId,
    Guid? ClientId,
    int? PageSize,
    int? PageNumber,
    ClientLoyaltyPointQueryNext.FilterSortKey? SortKey1,
    ClientLoyaltyPointQueryNext.FilterSortKey? SortKey2,
    bool? SortKey1Asc,
    bool? SortKey2Asc)
    : INextMultiProjectionListWithPagingQuery<ClientLoyaltyPointListProjection, ClientLoyaltyPointQueryNext,
        ClientLoyaltyPointQuery_Response>
{
    public enum FilterSortKey
    {
        BranchName,
        ClientName
    }

    public static ResultBox<IEnumerable<ClientLoyaltyPointQuery_Response>> HandleSort(
        IEnumerable<ClientLoyaltyPointQuery_Response> filteredList,
        ClientLoyaltyPointQueryNext query,
        IQueryContext context)
    {
        var sort = new Dictionary<FilterSortKey, bool>();
        if (query.SortKey1 != null) sort.Add(query.SortKey1.Value, query.SortKey1Asc ?? true);
        if (query.SortKey2 != null) sort.Add(query.SortKey2.Value, query.SortKey2Asc ?? true);
        if (sort.Count == 0)
            return ResultBox.FromValue(
                filteredList.OrderBy(m => m.BranchName).ThenBy(m => m.ClientName).AsEnumerable());
        var result = filteredList;
        foreach (var (sortKey, index) in sort.Select((item, index) => (item, index)))
            if (index == 0)
                result = sortKey.Value
                    ? result.OrderBy(
                        m => sortKey.Key switch
                        {
                            FilterSortKey.BranchName => m.BranchName,
                            FilterSortKey.ClientName => m.ClientName,
                            _ => throw new ArgumentOutOfRangeException()
                        })
                    : result.OrderByDescending(
                        m => sortKey.Key switch
                        {
                            FilterSortKey.BranchName => m.BranchName,
                            FilterSortKey.ClientName => m.ClientName,
                            _ => throw new ArgumentOutOfRangeException()
                        });
            else
                result = sortKey.Value
                    ? (result as IOrderedEnumerable<ClientLoyaltyPointQuery_Response> ??
                        throw new InvalidCastException()).ThenBy(
                        m => sortKey.Key switch
                        {
                            FilterSortKey.BranchName => m.BranchName,
                            FilterSortKey.ClientName => m.ClientName,
                            _ => throw new ArgumentOutOfRangeException()
                        })
                    : (result as IOrderedEnumerable<ClientLoyaltyPointQuery_Response> ??
                        throw new InvalidCastException()).ThenByDescending(
                        m => sortKey.Key switch
                        {
                            FilterSortKey.BranchName => m.BranchName,
                            FilterSortKey.ClientName => m.ClientName,
                            _ => throw new ArgumentOutOfRangeException()
                        });
        return ResultBox.FromValue(result);
    }

    public static ResultBox<IEnumerable<ClientLoyaltyPointQuery_Response>> HandleFilter(
        MultiProjectionState<ClientLoyaltyPointListProjection> projection,
        ClientLoyaltyPointQueryNext query,
        IQueryContext context)
    {
        var result = projection.Payload.Records.Select(
            m => new ClientLoyaltyPointQuery_Response(m.BranchId, m.BranchName, m.ClientId, m.ClientName, m.Point));
        if (query.BranchId.HasValue) result = result.Where(x => x.BranchId == query.BranchId.Value).ToImmutableList();
        if (query.ClientId.HasValue) result = result.Where(x => x.ClientId == query.ClientId.Value).ToImmutableList();
        return ResultBox.FromValue(result);
    }
}
