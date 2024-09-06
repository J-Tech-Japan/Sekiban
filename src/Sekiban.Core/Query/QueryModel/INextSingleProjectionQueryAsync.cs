using ResultBoxes;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextSingleProjectionQueryAsync<TSingleProjectionPayloadCommon, TQuery, TOutput> :
    INextSingleProjectionQueryCommon<TSingleProjectionPayloadCommon, TOutput>,
    INextQueryCommon<TQuery, TOutput> where TOutput : notnull
    where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon
    where TQuery : INextSingleProjectionQueryAsync<TSingleProjectionPayloadCommon, TQuery, TOutput>
{
    public QueryListType QueryListType => QueryListType.ActiveOnly;
    public Task<ResultBox<TOutput>> HandleFilterAsync(
        IEnumerable<SingleProjectionState<TSingleProjectionPayloadCommon>> list,
        IQueryContext context);
}
