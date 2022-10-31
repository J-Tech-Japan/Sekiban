using Sekiban.Core.Query.MultipleProjections;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Core.Query.QueryModel;

public interface IMultiProjectionQuery<in TProjection, TProjectionPayload, in TQueryParameter, TResponseQueryModel>
    where TProjection : MultiProjectionBase<TProjectionPayload>
    where TProjectionPayload : IMultiProjectionPayload, new()
    where TQueryParameter : IQueryParameter
{
    public TResponseQueryModel HandleFilter(TQueryParameter queryParam, MultiProjectionState<TProjectionPayload> projection);
}
