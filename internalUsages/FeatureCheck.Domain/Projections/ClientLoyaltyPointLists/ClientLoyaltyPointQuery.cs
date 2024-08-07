using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
using System.Collections.Immutable;
namespace FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;

public class ClientLoyaltyPointQuery : IMultiProjectionListQuery<ClientLoyaltyPointListProjection,
    ClientLoyaltyPointQuery.Parameter, ClientLoyaltyPointQuery_Response>
{
    public enum FilterSortKey
    {
        BranchName,
        ClientName
    }

    public IEnumerable<ClientLoyaltyPointQuery_Response> HandleSort(
        Parameter param,
        IEnumerable<ClientLoyaltyPointQuery_Response> filteredList)
    {
        var sort = new Dictionary<FilterSortKey, bool>();
        if (param.SortKey1 != null) sort.Add(param.SortKey1.Value, param.SortKey1Asc ?? true);
        if (param.SortKey2 != null) sort.Add(param.SortKey2.Value, param.SortKey2Asc ?? true);
        if (sort.Count == 0) return filteredList.OrderBy(m => m.BranchName).ThenBy(m => m.ClientName);
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
        return result;
    }

    public IEnumerable<ClientLoyaltyPointQuery_Response> HandleFilter(
        Parameter param,
        MultiProjectionState<ClientLoyaltyPointListProjection> projection)
    {
        var result = projection.Payload.Records.Select(
            m => new ClientLoyaltyPointQuery_Response(m.BranchId, m.BranchName, m.ClientId, m.ClientName, m.Point));
        if (param.BranchId.HasValue) result = result.Where(x => x.BranchId == param.BranchId.Value).ToImmutableList();
        if (param.ClientId.HasValue) result = result.Where(x => x.ClientId == param.ClientId.Value).ToImmutableList();
        return result;
    }

    public record Parameter(
        Guid? BranchId,
        Guid? ClientId,
        int? PageSize,
        int? PageNumber,
        FilterSortKey? SortKey1,
        FilterSortKey? SortKey2,
        bool? SortKey1Asc,
        bool? SortKey2Asc) : IListQueryPagingParameter<ClientLoyaltyPointQuery_Response>;
}
