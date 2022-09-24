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
    Dictionary<ClientLoyaltyPointQueryFilterSortKey, bool>? Sort) : IQueryFilterParameter<ClientLoyaltyPointQueryFilterSortKey>;
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
        if (queryParam.Sort == null)
        {
            return projections.OrderBy(m => m.BranchName).ThenBy(m => m.ClientName);
        }
        var result = projections;
        foreach (var (sortKey, index) in queryParam.Sort.Select((item, index) => (item, index)))
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
