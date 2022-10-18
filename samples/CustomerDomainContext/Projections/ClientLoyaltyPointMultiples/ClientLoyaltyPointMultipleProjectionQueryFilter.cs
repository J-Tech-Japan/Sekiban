using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using System.Collections.Immutable;
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
            Branches = projection.Contents.Branches.Where(x => x.BranchId == queryFilterParam.BranchId).ToImmutableList(),
            Records = projection.Contents.Records.Where(m => m.BranchId == queryFilterParam.BranchId).ToImmutableList()
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
                    ? response.Records.OrderBy(x => x.ClientName).ToImmutableList()
                    : response.Records.OrderByDescending(x => x.ClientName).ToImmutableList()
            };
        }
        if (queryFilterParam.SortKey == QuerySortKeys.Points)
        {
            return response with
            {
                Records = queryFilterParam.SortIsAsc
                    ? response.Records.OrderBy(x => x.Point).ToImmutableList()
                    : response.Records.OrderByDescending(x => x.Point).ToImmutableList()
            };
        }
        return response with { Records = response.Records.OrderBy(x => x.ClientName).ToImmutableList() };
    }
    public record QueryFilterParameter(Guid? BranchId, QuerySortKeys SortKey, bool SortIsAsc = true) : IQueryParameter;
}
