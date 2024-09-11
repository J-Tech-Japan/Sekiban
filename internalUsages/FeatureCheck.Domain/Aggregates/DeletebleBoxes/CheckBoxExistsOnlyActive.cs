using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.DeletebleBoxes;

public record CheckBoxExistsOnlyActive(string Code) : INextAggregateQuery<Box, CheckBoxExistsOnlyActive, bool>
{
    public static ResultBox<bool> HandleFilter(
        IEnumerable<AggregateState<Box>> list,
        CheckBoxExistsOnlyActive query,
        IQueryContext context) => list.Any(x => x.Payload.Code == query.Code);
}
