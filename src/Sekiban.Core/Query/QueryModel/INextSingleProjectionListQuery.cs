using ResultBoxes;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextSingleProjectionListQuery<TSingleProjectionPayloadCommon, TQuery, TOutput> :
    INextSingleProjectionQueryCommon<TSingleProjectionPayloadCommon, TOutput>,
    INextListQueryCommon<TQuery, TOutput> where TOutput : notnull
    where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon
    where TQuery : INextSingleProjectionListQuery<TSingleProjectionPayloadCommon, TQuery, TOutput>
{
    public QueryListType QueryListType => QueryListType.ActiveOnly;
    public ResultBox<IEnumerable<TOutput>> HandleFilter(
        IEnumerable<SingleProjectionState<TSingleProjectionPayloadCommon>> list,
        IQueryContext context);
    public ResultBox<IEnumerable<TOutput>> HandleSort(IEnumerable<TOutput> filteredList, IQueryContext context);
}
