using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextAggregateQueryAsync<TAggregatePayload, TQuery, TOutput> :
    INextAggregateQueryCommon<TAggregatePayload, TOutput>,
    INextQueryCommon<TQuery, TOutput>,
    INextQueryAsyncGeneral where TOutput : notnull
    where TAggregatePayload : IAggregatePayloadCommon
    where TQuery : INextAggregateQueryAsync<TAggregatePayload, TQuery, TOutput>, IEquatable<TQuery>
{
    public virtual static QueryListType GetQueryListType(TQuery query) => QueryListType.ActiveOnly;
    public static abstract Task<ResultBox<TOutput>> HandleFilterAsync(
        IEnumerable<AggregateState<TAggregatePayload>> list,
        TQuery query,
        IQueryContext context);
}
