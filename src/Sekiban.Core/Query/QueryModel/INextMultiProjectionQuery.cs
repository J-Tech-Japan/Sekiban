using ResultBoxes;
using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextMultiProjectionQuery<TMultiProjectionPayloadCommon, TOutput> :
    INextMultiProjectionQueryCommon<TMultiProjectionPayloadCommon, TOutput>,
    INextQueryCommon<TOutput> where TOutput : notnull
    where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon
{
    public ResultBox<TOutput> HandleFilter(
        MultiProjectionState<TMultiProjectionPayloadCommon> projection,
        IQueryContext context);
}
