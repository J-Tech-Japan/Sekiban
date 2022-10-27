using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Query.MultipleAggregate;

public interface IMultipleAggregateProjectionService
{

    public Task<MultipleAggregateProjectionState<TProjectionPayload>> GetProjectionAsync<TProjection, TProjectionPayload>()
        where TProjection : MultipleAggregateProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultipleAggregateProjectionPayload, new();
    public Task<MultipleAggregateProjectionState<SingleAggregateListProjectionState<AggregateState<TAggregatePayload>>>>
        GetAggregateListObject<TAggregatePayload>() where TAggregatePayload : IAggregatePayload, new();
    public Task<List<AggregateState<TAggregatePayload>>> GetAggregateList<TAggregatePayload>(QueryListType queryListType = QueryListType.ActiveOnly)
        where TAggregatePayload : IAggregatePayload, new();
    public
        Task<MultipleAggregateProjectionState<
            SingleAggregateListProjectionState<SingleAggregateProjectionState<TAggregateProjectionPayload>>>>
        GetSingleAggregateProjectionListObject<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload>()
        where TAggregate : IAggregatePayload, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload>,
        new()
        where TAggregateProjectionPayload : ISingleAggregateProjectionPayload;
    public Task<List<SingleAggregateProjectionState<TAggregateProjectionPayload>>>
        GetSingleAggregateProjectionList<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload>(
            QueryListType queryListType = QueryListType.ActiveOnly) where TAggregate : IAggregatePayload, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload>,
        new()
        where TAggregateProjectionPayload : ISingleAggregateProjectionPayload;
}
