using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextAggregateQuery<TAggregatePayload, TQuery, TOutput> : INextAggregateQueryCommon<TAggregatePayload, TOutput>,
    INextQueryCommon<TQuery, TOutput> where TOutput : notnull
    where TAggregatePayload : IAggregatePayloadCommon
    where TQuery : INextAggregateQuery<TAggregatePayload, TQuery, TOutput>
{
    public QueryListType QueryListType => QueryListType.ActiveOnly;
    public ResultBox<TOutput> HandleFilter(IEnumerable<AggregateState<TAggregatePayload>> list, IQueryContext context);
}
