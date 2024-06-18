using ResultBoxes;
using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextMultiProjectionListQueryAsync<TMultiProjectionPayloadCommon, TOutput> :
    INextMultiProjectionQueryCommon<TMultiProjectionPayloadCommon, TOutput>,
    INextListQueryCommon<TOutput> where TOutput : notnull where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon
{
    public Task<ResultBox<IEnumerable<TOutput>>> HandleFilterAsync(
        MultiProjectionState<TMultiProjectionPayloadCommon> projection,
        IQueryContext context);
    public Task<ResultBox<IEnumerable<TOutput>>> HandleSortAsync(IEnumerable<TOutput> filteredList, IQueryContext context);
}
