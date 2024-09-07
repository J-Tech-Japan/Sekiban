using ResultBoxes;
using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextMultiProjectionQueryAsync<TMultiProjectionPayloadCommon, TQuery, TOutput> :
    INextMultiProjectionQueryCommon<TMultiProjectionPayloadCommon, TOutput>,
    INextQueryCommon<TQuery, TOutput> where TOutput : notnull
    where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon
    where TQuery : INextMultiProjectionQueryAsync<TMultiProjectionPayloadCommon, TQuery, TOutput>
{
    public static abstract Task<ResultBox<TOutput>> HandleFilterAsync(
        MultiProjectionState<TMultiProjectionPayloadCommon> projection,
        TQuery query,
        IQueryContext context);
}
