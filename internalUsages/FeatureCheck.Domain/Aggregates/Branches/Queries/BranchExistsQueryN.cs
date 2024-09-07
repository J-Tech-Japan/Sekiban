using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.Branches.Queries;

public record BranchExistsQueryN(Guid BranchId) : INextAggregateQuery<Branch, BranchExistsQueryN, bool>,
    IQueryParameterMultiProjectionOptionSettable
{
    public static ResultBox<bool> HandleFilter(
        IEnumerable<AggregateState<Branch>> list,
        BranchExistsQueryN query,
        IQueryContext context) =>
        ResultBox.FromValue(list.Any(b => b.AggregateId == query.BranchId));
    public MultiProjectionRetrievalOptions? MultiProjectionRetrievalOptions { get; init; } = null;
}
