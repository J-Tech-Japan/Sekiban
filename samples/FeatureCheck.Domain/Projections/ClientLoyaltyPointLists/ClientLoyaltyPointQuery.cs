using System.Collections.Immutable;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;

namespace Customer.Domain.Projections.ClientLoyaltyPointLists;

public class ClientLoyaltyPointQuery : IMultiProjectionListQuery<ClientLoyaltyPointListProjection,
    ClientLoyaltyPointQuery.QueryParameter,
    ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord>
{
    public enum FilterSortKey
    {
        BranchName,
        ClientName
    }

    public IEnumerable<ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord> HandleSort(
        QueryParameter queryParam,
        IEnumerable<ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord> filteredList)
    {
        var sort = new Dictionary<FilterSortKey, bool>();
        if (queryParam.SortKey1 != null) sort.Add(queryParam.SortKey1.Value, queryParam.SortKey1Asc ?? true);
        if (queryParam.SortKey2 != null) sort.Add(queryParam.SortKey2.Value, queryParam.SortKey2Asc ?? true);
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
                    ? (result as IOrderedEnumerable<ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord> ??
                       throw new InvalidCastException()).ThenBy(
                        m => sortKey.Key switch
                        {
                            FilterSortKey.BranchName => m.BranchName,
                            FilterSortKey.ClientName => m.ClientName,
                            _ => throw new ArgumentOutOfRangeException()
                        })
                    : (result as IOrderedEnumerable<ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord> ??
                       throw new InvalidCastException()).ThenByDescending(
                        m => sortKey.Key switch
                        {
                            FilterSortKey.BranchName => m.BranchName,
                            FilterSortKey.ClientName => m.ClientName,
                            _ => throw new ArgumentOutOfRangeException()
                        });
        return result;
    }

    public IEnumerable<ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord> HandleFilter(
        QueryParameter queryParam,
        MultiProjectionState<ClientLoyaltyPointListProjection> projection)
    {
        var result = projection.Payload.Records;
        if (queryParam.BranchId.HasValue)
            result = result.Where(x => x.BranchId == queryParam.BranchId.Value).ToImmutableList();
        if (queryParam.ClientId.HasValue)
            result = result.Where(x => x.ClientId == queryParam.ClientId.Value).ToImmutableList();
        return result;
    }

    public record QueryParameter(
        Guid? BranchId,
        Guid? ClientId,
        int? PageSize,
        int? PageNumber,
        FilterSortKey? SortKey1,
        FilterSortKey? SortKey2,
        bool? SortKey1Asc,
        bool? SortKey2Asc) : IQueryPagingParameter;
}