using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.MultipleProjections;

public interface IMultiProjectionService
{

    public Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionAsync<TProjection, TProjectionPayload>()
        where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new();
    public Task<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>>
        GetAggregateListObject<TAggregatePayload>() where TAggregatePayload : IAggregatePayload, new();
    public Task<List<AggregateState<TAggregatePayload>>> GetAggregateList<TAggregatePayload>(QueryListType queryListType = QueryListType.ActiveOnly)
        where TAggregatePayload : IAggregatePayload, new();
    public
        Task<MultiProjectionState<
            SingleProjectionListState<SingleProjectionState<TAggregateProjectionPayload>>>>
        GetSingleProjectionListObject<TAggregatePayload, TSingleProjection, TAggregateProjectionPayload>()
        where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : SingleProjectionBase<TAggregatePayload, TSingleProjection, TAggregateProjectionPayload>,
        new()
        where TAggregateProjectionPayload : ISingleProjectionPayload;
    public Task<List<SingleProjectionState<TAggregateProjectionPayload>>>
        GetSingleProjectionList<TAggregatePayload, TSingleProjection, TAggregateProjectionPayload>(
            QueryListType queryListType = QueryListType.ActiveOnly) where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : SingleProjectionBase<TAggregatePayload, TSingleProjection, TAggregateProjectionPayload>,
        new()
        where TAggregateProjectionPayload : ISingleProjectionPayload;
}
