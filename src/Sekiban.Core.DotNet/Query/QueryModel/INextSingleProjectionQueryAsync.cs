using ResultBoxes;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextSingleProjectionQueryAsync<TSingleProjectionPayloadCommon, TQuery, TOutput> :
    INextSingleProjectionQueryCommon<TSingleProjectionPayloadCommon, TOutput>,
    INextQueryCommon<TQuery, TOutput> where TOutput : notnull
    where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon
    where TQuery : INextSingleProjectionQueryAsync<TSingleProjectionPayloadCommon, TQuery, TOutput>, IEquatable<TQuery>
{
    public virtual static QueryListType GetQueryListType(TQuery query) => QueryListType.ActiveOnly;
    public static abstract Task<ResultBox<TOutput>> HandleFilterAsync(
        IEnumerable<SingleProjectionState<TSingleProjectionPayloadCommon>> list,
        TQuery query,
        IQueryContext context);
}
