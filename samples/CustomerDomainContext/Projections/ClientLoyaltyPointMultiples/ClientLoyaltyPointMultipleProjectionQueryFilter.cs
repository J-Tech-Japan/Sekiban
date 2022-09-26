using Sekiban.EventSourcing.Queries.QueryModels;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
namespace CustomerDomainContext.Projections.ClientLoyaltyPointMultiples;

public enum ClientLoyaltyPointMultipleProjectionQuerySortKeys
{
    ClientName,
    Points
}
public record ClientLoyaltyPointMultipleProjectionQueryParameter(
    Guid? BranchId,
    ClientLoyaltyPointMultipleProjectionQuerySortKeys SortKey,
    bool SortIsAsc = true) : IQueryParameter;
public class ClientLoyaltyPointMultipleProjectionQueryFilter : IProjectionQueryFilterDefinition<ClientLoyaltyPointMultipleProjection,
    ClientLoyaltyPointMultipleProjectionQueryParameter, ClientLoyaltyPointMultipleProjection>
{

    public ClientLoyaltyPointMultipleProjection HandleFilter(
        ClientLoyaltyPointMultipleProjectionQueryParameter queryParam,
        ClientLoyaltyPointMultipleProjection projection)
    {
        if (queryParam.BranchId is null) { return projection; }
        return new ClientLoyaltyPointMultipleProjection
        {
            Branches = projection.Branches.Where(x => x.BranchId == queryParam.BranchId).ToList(),
            Records = projection.Records.Where(m => m.BranchId == queryParam.BranchId).ToList()
        };
    }
    public ClientLoyaltyPointMultipleProjection HandleSortAndPagingIfNeeded(
        ClientLoyaltyPointMultipleProjectionQueryParameter queryParam,
        ClientLoyaltyPointMultipleProjection response)
    {
        if (queryParam.SortKey == ClientLoyaltyPointMultipleProjectionQuerySortKeys.ClientName)
        {
            response.Records = queryParam.SortIsAsc
                ? response.Records.OrderBy(x => x.ClientName).ToList()
                : response.Records.OrderByDescending(x => x.ClientName).ToList();
        }
        else if (queryParam.SortKey == ClientLoyaltyPointMultipleProjectionQuerySortKeys.Points)
        {
            response.Records = queryParam.SortIsAsc
                ? response.Records.OrderBy(x => x.Point).ToList()
                : response.Records.OrderByDescending(x => x.Point).ToList();
        }
        return response;
    }
}
