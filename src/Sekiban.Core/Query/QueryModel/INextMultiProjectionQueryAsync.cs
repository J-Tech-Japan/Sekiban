using ResultBoxes;
using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextMultiProjectionQueryAsync<TMultiProjectionPayloadCommon, TOutput> : INextMultiProjectionQueryCommon<TMultiProjectionPayloadCommon, TOutput>,
    INextQueryCommon<TOutput> where TOutput : notnull where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon
{
    public Task<ResultBox<TOutput>> HandleFilterAsync(MultiProjectionState<TMultiProjectionPayloadCommon> projection, IQueryContext context);
}
