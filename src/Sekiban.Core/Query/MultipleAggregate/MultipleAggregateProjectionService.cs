using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultipleAggregate.MultipleProjection;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Query.MultipleAggregate;

public class MultipleAggregateProjectionService : IMultipleAggregateProjectionService
{
    private readonly IMultipleProjection _multipleProjection;

    public MultipleAggregateProjectionService(IMultipleProjection multipleProjection)
    {
        _multipleProjection = multipleProjection;
    }

    public Task<MultipleAggregateProjectionState<TProjectionPayload>> GetProjectionAsync<TProjection, TProjectionPayload>()
        where TProjection : MultipleAggregateProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultipleAggregateProjectionPayload, new()
    {
        return _multipleProjection.GetMultipleProjectionAsync<TProjection, TProjectionPayload>();
    }
    public async Task<MultipleAggregateProjectionState<SingleAggregateListProjectionState<AggregateState<TAggregatePayload>>>>
        GetAggregateListObject<TAggregatePayload>() where TAggregatePayload : IAggregatePayload, new()
    {
        var list = await _multipleProjection
            .GetMultipleProjectionAsync<SingleAggregateListProjector<Aggregate<TAggregatePayload>, AggregateState<TAggregatePayload>,
                    DefaultSingleAggregateProjector<TAggregatePayload>>
                , SingleAggregateListProjectionState<AggregateState<TAggregatePayload>>>();
        return list;
    }
    public async Task<List<AggregateState<TAggregatePayload>>> GetAggregateList<TAggregatePayload>(
        QueryListType queryListType = QueryListType.ActiveOnly) where TAggregatePayload : IAggregatePayload, new()
    {
        var projection = await GetAggregateListObject<TAggregatePayload>();
        return queryListType switch
        {
            QueryListType.ActiveAndDeleted => projection.Payload.List.ToList(),
            QueryListType.ActiveOnly => projection.Payload.List.Where(m => m.IsDeleted == false).ToList(),
            QueryListType.DeletedOnly => projection.Payload.List.Where(m => m.IsDeleted).ToList(),
            _ => projection.Payload.List.Where(m => m.IsDeleted == false).ToList()
        };
    }
    public
        Task<MultipleAggregateProjectionState<
            SingleAggregateListProjectionState<SingleAggregateProjectionState<TAggregateProjectionPayload>>>>
        GetSingleAggregateProjectionListObject<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload>()
        where TAggregate : IAggregatePayload, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload>,
        new()
        where TAggregateProjectionPayload : ISingleAggregateProjectionPayload
    {
        return _multipleProjection
            .GetMultipleProjectionAsync<
                SingleAggregateListProjector<TSingleAggregateProjection, SingleAggregateProjectionState<TAggregateProjectionPayload>,
                    TSingleAggregateProjection>,
                SingleAggregateListProjectionState<SingleAggregateProjectionState<TAggregateProjectionPayload>>>();
    }
    public async Task<List<SingleAggregateProjectionState<TAggregateProjectionPayload>>>
        GetSingleAggregateProjectionList<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload>(
            QueryListType queryListType = QueryListType.ActiveOnly) where TAggregate : IAggregatePayload, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload>,
        new()
        where TAggregateProjectionPayload : ISingleAggregateProjectionPayload
    {
        var projection = await GetSingleAggregateProjectionListObject<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload>();
        return queryListType switch
        {
            QueryListType.ActiveAndDeleted => projection.Payload.List.ToList(),
            QueryListType.ActiveOnly => projection.Payload.List.Where(m => m.GetIsDeleted() == false).ToList(),
            QueryListType.DeletedOnly => projection.Payload.List.Where(m => m.GetIsDeleted()).ToList(),
            _ => projection.Payload.List.Where(m => m.GetIsDeleted() == false).ToList()
        };
    }
}
