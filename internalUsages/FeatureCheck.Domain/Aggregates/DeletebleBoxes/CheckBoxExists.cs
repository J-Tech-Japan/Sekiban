using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.DeletebleBoxes;

public record CheckBoxExists(string Code) : INextAggregateQuery<Box, CheckBoxExists, bool>
{
    public static QueryListType GetQueryListType(CheckBoxExists query) => QueryListType.ActiveAndDeleted;
    public static ResultBox<bool> HandleFilter(
        IEnumerable<AggregateState<Box>> list,
        CheckBoxExists query,
        IQueryContext context) => list.Any(x => x.Payload.Code == query.Code);
}
