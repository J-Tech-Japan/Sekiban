using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultipleProjections.Projections;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.MultipleProjections;

public class MultiProjectionService : IMultiProjectionService
{
    private readonly IMultipleProjection _multipleProjection;

    public MultiProjectionService(IMultipleProjection multipleProjection) => _multipleProjection = multipleProjection;

    public Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionAsync<TProjection, TProjectionPayload>()
        where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new() =>
        _multipleProjection.GetMultipleProjectionAsync<TProjection, TProjectionPayload>();
    public async Task<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>>
        GetAggregateListObject<TAggregatePayload>() where TAggregatePayload : IAggregatePayload, new()
    {
        var list = await _multipleProjection
            .GetMultipleProjectionAsync<SingleProjectionListProjector<Aggregate<TAggregatePayload>, AggregateState<TAggregatePayload>,
                    DefaultSingleProjector<TAggregatePayload>>
                , SingleProjectionListState<AggregateState<TAggregatePayload>>>();
        return list;
    }
    public async Task<List<AggregateState<TAggregatePayload>>> GetAggregateList<TAggregatePayload>(
        QueryListType queryListType = QueryListType.ActiveOnly) where TAggregatePayload : IAggregatePayload, new()
    {
        var projection = await GetAggregateListObject<TAggregatePayload>();
        return queryListType switch
        {
            QueryListType.ActiveAndDeleted => projection.Payload.List.ToList(),
            QueryListType.ActiveOnly => projection.Payload.List.Where(m => m.GetIsDeleted() == false).ToList(),
            QueryListType.DeletedOnly => projection.Payload.List.Where(m => m.GetIsDeleted()).ToList(),
            _ => projection.Payload.List.Where(m => m.GetIsDeleted() == false).ToList()
        };
    }
    public
        Task<MultiProjectionState<
            SingleProjectionListState<SingleProjectionState<TAggregateProjectionPayload>>>>
        GetSingleProjectionListObject<TAggregate, TSingleProjection, TAggregateProjectionPayload>()
        where TAggregate : IAggregatePayload, new()
        where TSingleProjection : SingleProjectionBase<TAggregate, TSingleProjection, TAggregateProjectionPayload>,
        new()
        where TAggregateProjectionPayload : ISingleProjectionPayload => _multipleProjection
        .GetMultipleProjectionAsync<
            SingleProjectionListProjector<TSingleProjection, SingleProjectionState<TAggregateProjectionPayload>,
                TSingleProjection>,
            SingleProjectionListState<SingleProjectionState<TAggregateProjectionPayload>>>();
    public async Task<List<SingleProjectionState<TAggregateProjectionPayload>>>
        GetSingleProjectionList<TAggregate, TSingleProjection, TAggregateProjectionPayload>(
            QueryListType queryListType = QueryListType.ActiveOnly) where TAggregate : IAggregatePayload, new()
        where TSingleProjection : SingleProjectionBase<TAggregate, TSingleProjection, TAggregateProjectionPayload>,
        new()
        where TAggregateProjectionPayload : ISingleProjectionPayload
    {
        var projection = await GetSingleProjectionListObject<TAggregate, TSingleProjection, TAggregateProjectionPayload>();
        return queryListType switch
        {
            QueryListType.ActiveAndDeleted => projection.Payload.List.ToList(),
            QueryListType.ActiveOnly => projection.Payload.List.Where(m => m.GetIsDeleted() == false).ToList(),
            QueryListType.DeletedOnly => projection.Payload.List.Where(m => m.GetIsDeleted()).ToList(),
            _ => projection.Payload.List.Where(m => m.GetIsDeleted() == false).ToList()
        };
    }
}
