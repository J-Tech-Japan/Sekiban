using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface INextAggregateQueryAsync<TAggregatePayload, TOutput> : INextAggregateQueryCommon<TAggregatePayload, TOutput>,
    INextQueryCommon<TOutput>, INextQueryAsyncGeneral where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon
{
    public QueryListType QueryListType => QueryListType.ActiveOnly;
    public Task<ResultBox<TOutput>> HandleFilterAsync(IEnumerable<AggregateState<TAggregatePayload>> list, IQueryContext context);
}
public interface
    INextSingleProjectionQueryAsync<TSingleProjectionPayloadCommon, TOutput> :
    INextSingleProjectionQueryCommon<TSingleProjectionPayloadCommon, TOutput>,
    INextQueryCommon<TOutput> where TOutput : notnull where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon
{
    public QueryListType QueryListType => QueryListType.ActiveOnly;
    public Task<ResultBox<TOutput>> HandleFilterAsync(IEnumerable<SingleProjectionState<TSingleProjectionPayloadCommon>> list, IQueryContext context);
}
