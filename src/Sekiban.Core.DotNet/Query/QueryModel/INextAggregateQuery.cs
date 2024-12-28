using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextAggregateQuery<TAggregatePayload, TQuery, TOutput> : INextAggregateQueryCommon<TAggregatePayload, TOutput>,
    INextQueryCommon<TQuery, TOutput> where TOutput : notnull
    where TAggregatePayload : IAggregatePayloadCommon
    where TQuery : INextAggregateQuery<TAggregatePayload, TQuery, TOutput>, IEquatable<TQuery>
{
    public virtual static QueryListType GetQueryListType(TQuery query) => QueryListType.ActiveOnly;
    public static abstract ResultBox<TOutput> HandleFilter(
        IEnumerable<AggregateState<TAggregatePayload>> list,
        TQuery query,
        IQueryContext context);
}
