using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextAggregateListQuery<TAggregatePayload, TQuery, TOutput> : INextAggregateQueryCommon<TAggregatePayload, TOutput>,
    INextListQueryCommon<TQuery, TOutput> where TOutput : notnull
    where TAggregatePayload : IAggregatePayloadCommon
    where TQuery : INextAggregateListQuery<TAggregatePayload, TQuery, TOutput>
{
    public virtual static QueryListType GetQueryListType(TQuery query) => QueryListType.ActiveOnly;
    public static abstract ResultBox<IEnumerable<TOutput>> HandleFilter(
        IEnumerable<AggregateState<TAggregatePayload>> list,
        TQuery query,
        IQueryContext context);
    public static abstract ResultBox<IEnumerable<TOutput>> HandleSort(
        IEnumerable<TOutput> filteredList,
        TQuery query,
        IQueryContext context);
}
