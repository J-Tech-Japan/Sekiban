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
    public ResultBox<IEnumerable<TOutput>> HandleFilter(
        MultiProjectionState<TMultiProjectionPayloadCommon> projection,
        IQueryContext context);
    public ResultBox<IEnumerable<TOutput>> HandleSort(IEnumerable<TOutput> filteredList, IQueryContext context);
}
