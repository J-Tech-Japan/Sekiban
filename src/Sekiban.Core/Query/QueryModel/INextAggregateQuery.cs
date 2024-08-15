using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextAggregateQuery<TAggregatePayload, TOutput> : INextAggregateQueryCommon<TAggregatePayload, TOutput>,
    INextQueryCommon<TOutput> where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon
{
    public QueryListType QueryListType => QueryListType.ActiveOnly;
    public ResultBox<TOutput> HandleFilter(IEnumerable<AggregateState<TAggregatePayload>> list, IQueryContext context);
}
