using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.QueryModels;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
namespace CustomerDomainContext.Projections.ClientLoyaltyPointMultiples;

public class ClientLoyaltyPointMultipleProjectionQueryFilter : IProjectionQueryFilterDefinition<ClientLoyaltyPointMultipleProjection,
    ClientLoyaltyPointMultipleProjection.ContentsDefinition, ClientLoyaltyPointMultipleProjectionQueryFilter.QueryFilterParameter,
    ClientLoyaltyPointMultipleProjection.ContentsDefinition>
{
    public enum QuerySortKeys
    {
        ClientName,
        Points
    }
    public ClientLoyaltyPointMultipleProjection.ContentsDefinition HandleFilter(
        QueryFilterParameter queryFilterParam,
        MultipleAggregateProjectionContentsDto<ClientLoyaltyPointMultipleProjection.ContentsDefinition> projection)
    {
        if (queryFilterParam.BranchId is null) { return projection.Contents; }
        return new ClientLoyaltyPointMultipleProjection.ContentsDefinition
        {
            Branches = projection.Contents.Branches.Where(x => x.BranchId == queryFilterParam.BranchId).ToList(),
            Records = projection.Contents.Records.Where(m => m.BranchId == queryFilterParam.BranchId).ToList()
        };
    }
    public ClientLoyaltyPointMultipleProjection.ContentsDefinition HandleSortAndPagingIfNeeded(
        QueryFilterParameter queryFilterParam,
        ClientLoyaltyPointMultipleProjection.ContentsDefinition response)
    {
        if (queryFilterParam.SortKey == QuerySortKeys.ClientName)
        {
            return response with
            {
                Records = queryFilterParam.SortIsAsc
                    ? response.Records.OrderBy(x => x.ClientName).ToList()
                    : response.Records.OrderByDescending(x => x.ClientName).ToList()
            };
        }
        if (queryFilterParam.SortKey == QuerySortKeys.Points)
        {
            return response with
            {
                Records = queryFilterParam.SortIsAsc
                    ? response.Records.OrderBy(x => x.Point).ToList()
                    : response.Records.OrderByDescending(x => x.Point).ToList()
            };
        }
        return response with { Records = response.Records.OrderBy(x => x.ClientName).ToList() };
    }
    public record QueryFilterParameter(Guid? BranchId, QuerySortKeys SortKey, bool SortIsAsc = true) : IQueryParameter;
}