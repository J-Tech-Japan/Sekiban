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
    public static abstract Task<ResultBox<IEnumerable<TOutput>>> HandleFilterAsync(
        IEnumerable<SingleProjectionState<TSingleProjectionPayloadCommon>> list,
        TQuery query,
        IQueryContext context);
    public static abstract Task<ResultBox<IEnumerable<TOutput>>> HandleSortAsync(
        IEnumerable<TOutput> filteredList,
        TQuery query,
        IQueryContext context);
}
