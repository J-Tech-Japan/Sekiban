using ResultBoxes;
using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    INextMultiProjectionListQueryAsync<TMultiProjectionPayloadCommon, TQuery, TOutput> :
    INextMultiProjectionQueryCommon<TMultiProjectionPayloadCommon, TOutput>,
    INextListQueryCommon<TQuery, TOutput> where TOutput : notnull
    where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon
    where TQuery : INextMultiProjectionListQueryAsync<TMultiProjectionPayloadCommon, TQuery, TOutput>
{
    public Task<ResultBox<IEnumerable<TOutput>>> HandleFilterAsync(
        MultiProjectionState<TMultiProjectionPayloadCommon> projection,
        IQueryContext context);
    public Task<ResultBox<IEnumerable<TOutput>>> HandleSortAsync(
        IEnumerable<TOutput> filteredList,
        IQueryContext context);
}
