using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface INextAggregateQuery<TAggregatePayload, TOutput> : INextAggregateQueryCommon<TAggregatePayload, TOutput>, INextQueryCommon<TOutput>
    where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon
{
    public QueryListType QueryListType => QueryListType.ActiveOnly;
    public ResultBox<TOutput> HandleFilter(IEnumerable<AggregateState<TAggregatePayload>> list, IQueryContext context);
}
public interface
    INextSingleProjectionQuery<TSingleProjectionPayloadCommon, TOutput> : INextSingleProjectionQueryCommon<TSingleProjectionPayloadCommon, TOutput>,
    INextQueryCommon<TOutput> where TOutput : notnull where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon
{
    public QueryListType QueryListType => QueryListType.ActiveOnly;
    public ResultBox<TOutput> HandleFilter(IEnumerable<SingleProjectionState<TSingleProjectionPayloadCommon>> list, IQueryContext context);
}
