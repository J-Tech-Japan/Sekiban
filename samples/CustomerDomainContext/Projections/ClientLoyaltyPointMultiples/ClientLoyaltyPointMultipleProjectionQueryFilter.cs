using Sekiban.EventSourcing.Queries.MultipleAggregates;
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
    ClientLoyaltyPointMultipleProjection.ContentsDefinition, ClientLoyaltyPointMultipleProjectionQueryParameter,
    ClientLoyaltyPointMultipleProjection.ContentsDefinition>
{

    public ClientLoyaltyPointMultipleProjection.ContentsDefinition HandleFilter(
        ClientLoyaltyPointMultipleProjectionQueryParameter queryParam,
        MultipleAggregateProjectionContentsDto<ClientLoyaltyPointMultipleProjection.ContentsDefinition> projection)
    {
        if (queryParam.BranchId is null) { return projection.Contents; }
        return new ClientLoyaltyPointMultipleProjection.ContentsDefinition
        {
            Branches = projection.Contents.Branches.Where(x => x.BranchId == queryParam.BranchId).ToList(),
            Records = projection.Contents.Records.Where(m => m.BranchId == queryParam.BranchId).ToList()
        };
    }
    public ClientLoyaltyPointMultipleProjection.ContentsDefinition HandleSortAndPagingIfNeeded(
        ClientLoyaltyPointMultipleProjectionQueryParameter queryParam,
        ClientLoyaltyPointMultipleProjection.ContentsDefinition response)
    {
        if (queryParam.SortKey == ClientLoyaltyPointMultipleProjectionQuerySortKeys.ClientName)
        {
            return response with
            {
                Records = queryParam.SortIsAsc
                    ? response.Records.OrderBy(x => x.ClientName).ToList()
                    : response.Records.OrderByDescending(x => x.ClientName).ToList()
            };
        }
        if (queryParam.SortKey == ClientLoyaltyPointMultipleProjectionQuerySortKeys.Points)
        {
            return response with
            {
                Records = queryParam.SortIsAsc
                    ? response.Records.OrderBy(x => x.Point).ToList()
                    : response.Records.OrderByDescending(x => x.Point).ToList()
            };
        }
        return response with { Records = response.Records.OrderBy(x => x.ClientName).ToList() };
    }
}
