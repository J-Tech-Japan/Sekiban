using ResultBoxes;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextSingleProjectionListQueryAsync<TSingleProjectionPayloadCommon, TQuery, TOutput> :
    INextSingleProjectionQueryCommon<TSingleProjectionPayloadCommon, TOutput>,
    INextListQueryCommon<TQuery, TOutput> where TOutput : notnull
    where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon
    where TQuery : INextSingleProjectionListQueryAsync<TSingleProjectionPayloadCommon, TQuery, TOutput>
{
    public QueryListType QueryListType => QueryListType.ActiveOnly;
    public Task<ResultBox<IEnumerable<TOutput>>> HandleFilterAsync(
        IEnumerable<SingleProjectionState<TSingleProjectionPayloadCommon>> list,
        IQueryContext context);
    public Task<ResultBox<IEnumerable<TOutput>>> HandleSortAsync(
        IEnumerable<TOutput> filteredList,
        IQueryContext context);
}
