using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextAggregateListQueryAsync<TAggregatePayload, TQuery, TOutput> :
    INextAggregateQueryCommon<TAggregatePayload, TOutput>,
    INextListQueryCommon<TQuery, TOutput>,
    INextQueryAsyncGeneral where TOutput : notnull
    where TAggregatePayload : IAggregatePayloadCommon
    where TQuery : INextAggregateListQueryAsync<TAggregatePayload, TQuery, TOutput>
{
    public virtual static QueryListType GetQueryListType(TQuery query) => QueryListType.ActiveOnly;
    public static abstract Task<ResultBox<IEnumerable<TOutput>>> HandleFilterAsync(
        IEnumerable<AggregateState<TAggregatePayload>> list,
        TQuery query,
        IQueryContext context);
    public static abstract Task<ResultBox<IEnumerable<TOutput>>> HandleSortAsync(
        IEnumerable<TOutput> filteredList,
        TQuery query,
        IQueryContext context);
}
