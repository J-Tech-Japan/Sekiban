using Sekiban.EventSourcing.Queries.QueryModels;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
namespace CustomerDomainContext.Aggregates.Branches.QueryFilters;

public class BranchExistsQueryFilter : IAggregateQueryFilterDefinition<Branch, BranchContents, BranchExistsQueryFilter.QueryParameter, bool>
{
    public bool HandleFilter(QueryParameter queryParam, IEnumerable<AggregateDto<BranchContents>> list)
    {
        return list.Any(b => b.AggregateId == queryParam.BranchId);
    }
    public bool HandleSort(QueryParameter queryParam, bool projections)
    {
        return projections;
    }
    public record QueryParameter(Guid BranchId) : IQueryParameter;
}