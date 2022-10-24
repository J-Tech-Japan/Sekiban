using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Customer.Domain.Aggregates.Branches.QueryFilters;

public class BranchExistsQueryFilter : IAggregateQueryFilterDefinition<Branch, BranchPayload, BranchExistsQueryFilter.QueryParameter, bool>
{
    public bool HandleFilter(QueryParameter queryParam, IEnumerable<AggregateState<BranchPayload>> list)
    {
        return list.Any(b => b.AggregateId == queryParam.BranchId);
    }
    public bool HandleSort(QueryParameter queryParam, bool projections)
    {
        return projections;
    }
    public record QueryParameter(Guid BranchId) : IQueryParameter;
}
