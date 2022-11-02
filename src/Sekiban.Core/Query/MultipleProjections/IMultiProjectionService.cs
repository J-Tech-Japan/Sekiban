using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.MultipleProjections;

public interface IMultiProjectionService
{

    public Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionAsync<TProjection, TProjectionPayload>()
        where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new();
    public Task<MultiProjectionState<SingleProjectionListState<AggregateIdentifierState<TAggregatePayload>>>>
        GetAggregateListObject<TAggregatePayload>() where TAggregatePayload : IAggregatePayload, new();
    public Task<List<AggregateIdentifierState<TAggregatePayload>>> GetAggregateList<TAggregatePayload>(
        QueryListType queryListType = QueryListType.ActiveOnly)
        where TAggregatePayload : IAggregatePayload, new();
    public
        Task<MultiProjectionState<
            SingleProjectionListState<ProjectionState<TSingleProjectionPayload>>>>
        GetSingleProjectionListObject<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>()
        where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : ProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>,
        new()
        where TSingleProjectionPayload : ISingleProjectionPayload;
    public Task<List<ProjectionState<TSingleProjectionPayload>>>
        GetSingleProjectionList<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>(
            QueryListType queryListType = QueryListType.ActiveOnly) where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : ProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>,
        new()
        where TSingleProjectionPayload : ISingleProjectionPayload;
}
