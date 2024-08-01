using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.Branches.Queries;

public class BranchExistsQuery : IAggregateQuery<Branch, BranchExistsQuery.Parameter, BranchExistsQuery.Response>
{
    public Response HandleFilter(Parameter param, IEnumerable<AggregateState<Branch>> list)
    {
        return new Response(list.Any(b => b.AggregateId == param.BranchId));
    }

    public record Parameter(Guid BranchId) : IQueryParameter<Response>, IQueryParameterMultiProjectionOptionSettable
    {
        public MultiProjectionRetrievalOptions? MultiProjectionRetrievalOptions { get; init; } = null;
    }

    public record Response(bool Exists) : IQueryResponse;
}
public record BranchExistsQueryN(Guid BranchId)
    : INextAggregateQuery<Branch, bool>, IQueryParameterMultiProjectionOptionSettable
{
    public ResultBox<bool> HandleFilter(IEnumerable<AggregateState<Branch>> list, IQueryContext context)
    {
        return ResultBox.FromValue(list.Any(b => b.AggregateId == BranchId));
    }

    public MultiProjectionRetrievalOptions? MultiProjectionRetrievalOptions { get; init; } = null;
}
