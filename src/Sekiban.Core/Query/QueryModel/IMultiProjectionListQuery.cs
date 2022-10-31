using Sekiban.Core.Query.MultipleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface IMultiProjectionListQuery<in TProjection, TProjectionPayload, in TQueryParameter, TResponseQueryModel>
    where TProjection : MultiProjectionBase<TProjectionPayload> where TProjectionPayload : IMultiProjectionPayload, new()
{
    public IEnumerable<TResponseQueryModel> HandleFilter(
        TQueryParameter queryParam,
        MultiProjectionState<TProjectionPayload> projection);
    public IEnumerable<TResponseQueryModel> HandleSort(TQueryParameter queryParam, IEnumerable<TResponseQueryModel> projections);
}
