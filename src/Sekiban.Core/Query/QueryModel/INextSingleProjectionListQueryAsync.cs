using ResultBoxes;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextSingleProjectionListQueryAsync<TSingleProjectionPayloadCommon, TOutput> :
    INextSingleProjectionQueryCommon<TSingleProjectionPayloadCommon, TOutput>,
    INextListQueryCommon<TOutput> where TOutput : notnull
    where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon
{
    public QueryListType QueryListType => QueryListType.ActiveOnly;
    public Task<ResultBox<IEnumerable<TOutput>>> HandleFilterAsync(
        IEnumerable<SingleProjectionState<TSingleProjectionPayloadCommon>> list,
        IQueryContext context);
    public Task<ResultBox<IEnumerable<TOutput>>> HandleSortAsync(
        IEnumerable<TOutput> filteredList,
        IQueryContext context);
}
