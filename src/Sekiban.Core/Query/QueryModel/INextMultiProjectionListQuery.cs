using ResultBoxes;
using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextMultiProjectionListQuery<TMultiProjectionPayloadCommon, TOutput> : INextMultiProjectionQueryCommon<TMultiProjectionPayloadCommon, TOutput>,
    INextListQueryCommon<TOutput> where TOutput : notnull where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon
{
    public ResultBox<IEnumerable<TOutput>> HandleFilter(MultiProjectionState<TMultiProjectionPayloadCommon> projection, IQueryContext context);
    public ResultBox<IEnumerable<TOutput>> HandleSort(IEnumerable<TOutput> filteredList, IQueryContext context);
}
