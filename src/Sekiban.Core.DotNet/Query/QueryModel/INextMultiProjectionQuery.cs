using ResultBoxes;
using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextMultiProjectionQuery<TMultiProjectionPayloadCommon, TQuery, TOutput> :
    INextMultiProjectionQueryCommon<TMultiProjectionPayloadCommon, TOutput>,
    INextQueryCommon<TQuery, TOutput> where TOutput : notnull
    where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon
    where TQuery : INextMultiProjectionQuery<TMultiProjectionPayloadCommon, TQuery, TOutput>, IEquatable<TQuery>
{
    public static abstract ResultBox<TOutput> HandleFilter(
        MultiProjectionState<TMultiProjectionPayloadCommon> projection,
        TQuery query,
        IQueryContext context);
}
