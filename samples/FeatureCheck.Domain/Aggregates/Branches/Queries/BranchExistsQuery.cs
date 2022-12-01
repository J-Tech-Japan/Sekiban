using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Shared;

namespace Customer.Domain.Aggregates.Branches.Queries;

public class BranchExistsQuery : IAggregateQuery<Branch, BranchExistsQuery.QueryParameter, bool>
{
    private readonly ISekibanDateProducer _dateProducer;

    public BranchExistsQuery(ISekibanDateProducer dateProducer)
    {
        _dateProducer = dateProducer;
    }

    public bool HandleFilter(QueryParameter queryParam, IEnumerable<AggregateState<Branch>> list)
    {
        return list.Any(b => b.AggregateId == queryParam.BranchId);
    }

    public bool HandleSort(QueryParameter queryParam, bool projections)
    {
        return projections;
    }

    public record QueryParameter(Guid BranchId) : IQueryParameter;
}
