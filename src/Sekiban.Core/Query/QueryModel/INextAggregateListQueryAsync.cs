using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface INextAggregateListQueryAsync<TAggregatePayload, TOutput> : INextAggregateQueryCommon<TAggregatePayload, TOutput>,
    INextListQueryCommon<TOutput>, INextQueryAsyncGeneral where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon
{
    public QueryListType QueryListType => QueryListType.ActiveOnly;
    public Task<ResultBox<IEnumerable<TOutput>>> HandleFilterAsync(IEnumerable<AggregateState<TAggregatePayload>> list, IQueryContext context);
    public Task<ResultBox<IEnumerable<TOutput>>> HandleSortAsync(IEnumerable<TOutput> filteredList, IQueryContext context);
}
