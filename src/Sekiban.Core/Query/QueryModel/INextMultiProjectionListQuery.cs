using ResultBoxes;
using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextMultiProjectionListQuery<TMultiProjectionPayloadCommon, TQuery, TOutput> :
    INextMultiProjectionQueryCommon<TMultiProjectionPayloadCommon, TOutput>,
    INextListQueryCommon<TQuery, TOutput> where TOutput : notnull
    where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon
    where TQuery : INextMultiProjectionListQuery<TMultiProjectionPayloadCommon, TQuery, TOutput>
{
    public static abstract ResultBox<IEnumerable<TOutput>> HandleFilter(
        MultiProjectionState<TMultiProjectionPayloadCommon> projection,
        TQuery query,
        IQueryContext context);
    public static abstract ResultBox<IEnumerable<TOutput>> HandleSort(
        IEnumerable<TOutput> filteredList,
        TQuery query,
        IQueryContext context);
}
