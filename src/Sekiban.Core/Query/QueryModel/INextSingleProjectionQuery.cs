using ResultBoxes;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextSingleProjectionQuery<TSingleProjectionPayloadCommon, TQuery, TOutput> :
    INextSingleProjectionQueryCommon<TSingleProjectionPayloadCommon, TOutput>,
    INextQueryCommon<TQuery, TOutput> where TOutput : notnull
    where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon
    where TQuery : INextSingleProjectionQuery<TSingleProjectionPayloadCommon, TQuery, TOutput>
{
    public QueryListType QueryListType => QueryListType.ActiveOnly;
    public ResultBox<TOutput> HandleFilter(
        IEnumerable<SingleProjectionState<TSingleProjectionPayloadCommon>> list,
        IQueryContext context);
}
