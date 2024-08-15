using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.Branches.Queries;

public record BranchExistsQueryN(Guid BranchId)
    : INextAggregateQuery<Branch, bool>, IQueryParameterMultiProjectionOptionSettable
{
    public ResultBox<bool> HandleFilter(IEnumerable<AggregateState<Branch>> list, IQueryContext context)
    {
        return ResultBox.FromValue(list.Any(b => b.AggregateId == BranchId));
    }

    public MultiProjectionRetrievalOptions? MultiProjectionRetrievalOptions { get; init; } = null;
}