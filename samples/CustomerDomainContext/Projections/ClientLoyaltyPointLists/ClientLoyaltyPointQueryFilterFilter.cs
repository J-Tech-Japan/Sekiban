using Sekiban.EventSourcing.Queries.QueryModels;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
namespace CustomerDomainContext.Projections.ClientLoyaltyPointLists;

public enum ClientLoyaltyPointQueryFilterSortKey
{
    BranchName,
    ClientName
}
public record ClientLoyaltyPointQueryFilterParameter(
    Guid? BranchId,
    Guid? ClientId,
    int? PageSize,
    int? PageNumber,
    ClientLoyaltyPointQueryFilterSortKey? SortKey1,
    ClientLoyaltyPointQueryFilterSortKey? SortKey2,
    bool? SortKey1Asc,
    bool? SortKey2Asc) : IQueryFilterParameter;
public class ClientLoyaltyPointQueryFilterFilter : IProjectionListQueryFilterDefinition<ClientLoyaltyPointListProjection,
    ClientLoyaltyPointQueryFilterParameter, ClientLoyaltyPointListRecord>
{
    public IEnumerable<ClientLoyaltyPointListRecord> HandleFilter(
        ClientLoyaltyPointQueryFilterParameter queryParam,
        ClientLoyaltyPointListProjection projection)
    {
        var result = projection.Records;
        if (queryParam.BranchId.HasValue)
        {
            result = result.Where(x => x.BranchId == queryParam.BranchId.Value).ToList();
        }
        if (queryParam.ClientId.HasValue)
        {
            result = result.Where(x => x.ClientId == queryParam.ClientId.Value).ToList();
        }
        return result;
    }
    public IEnumerable<ClientLoyaltyPointListRecord> HandleSort(
        ClientLoyaltyPointQueryFilterParameter queryParam,
        IEnumerable<ClientLoyaltyPointListRecord> projections)
    {
        var sort = new Dictionary<ClientLoyaltyPointQueryFilterSortKey, bool>();
        if (queryParam.SortKey1 != null) { sort.Add(queryParam.SortKey1.Value, queryParam.SortKey1Asc ?? true); }
        if (queryParam.SortKey2 != null) { sort.Add(queryParam.SortKey2.Value, queryParam.SortKey2Asc ?? true); }
        if (sort.Count == 0)
        {
            return projections.OrderBy(m => m.BranchName).ThenBy(m => m.ClientName);
        }
        var result = projections;
        foreach (var (sortKey, index) in sort.Select((item, index) => (item, index)))
        {
            if (index == 0)
            {
                result = sortKey.Value
                    ? result.OrderBy(
                        m => sortKey.Key switch
                        {
                            ClientLoyaltyPointQueryFilterSortKey.BranchName => m.BranchName,
                            ClientLoyaltyPointQueryFilterSortKey.ClientName => m.ClientName,
                            _ => throw new ArgumentOutOfRangeException()
                        })
                    : result.OrderByDescending(
                        m => sortKey.Key switch
                        {
                            ClientLoyaltyPointQueryFilterSortKey.BranchName => m.BranchName,
                            ClientLoyaltyPointQueryFilterSortKey.ClientName => m.ClientName,
                            _ => throw new ArgumentOutOfRangeException()
                        });
            } else
            {
                result = sortKey.Value
                    ? (result as IOrderedEnumerable<ClientLoyaltyPointListRecord> ?? throw new InvalidCastException()).ThenBy(
                        m => sortKey.Key switch
                        {
                            ClientLoyaltyPointQueryFilterSortKey.BranchName => m.BranchName,
                            ClientLoyaltyPointQueryFilterSortKey.ClientName => m.ClientName,
                            _ => throw new ArgumentOutOfRangeException()
                        })
                    : (result as IOrderedEnumerable<ClientLoyaltyPointListRecord> ?? throw new InvalidCastException()).ThenByDescending(
                        m => sortKey.Key switch
                        {
                            ClientLoyaltyPointQueryFilterSortKey.BranchName => m.BranchName,
                            ClientLoyaltyPointQueryFilterSortKey.ClientName => m.ClientName,
                            _ => throw new ArgumentOutOfRangeException()
                        });
            }
        }
        return result;
    }
}
