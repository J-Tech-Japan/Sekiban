using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultProjections.Projections;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.MultProjections;

public class MultiProjectionService : IMultiProjectionService
{
    private readonly IMultiProjection multiProjection;

    public MultiProjectionService(IMultiProjection multiProjection) => this.multiProjection = multiProjection;

    public Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionAsync<TProjection, TProjectionPayload>()
        where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new() =>
        multiProjection.GetMultiProjectionAsync<TProjection, TProjectionPayload>();
    public async Task<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>>
        GetAggregateListObject<TAggregatePayload>() where TAggregatePayload : IAggregatePayload, new()
    {
        var list = await multiProjection
            .GetMultiProjectionAsync<SingleProjectionListProjector<Aggregate<TAggregatePayload>,
                    AggregateState<TAggregatePayload>,
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
            SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>
        GetSingleProjectionListObject<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>()
        where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : MultiProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>,
        new()
        where TSingleProjectionPayload : ISingleProjectionPayload => multiProjection
        .GetMultiProjectionAsync<
            SingleProjectionListProjector<TSingleProjection, SingleProjectionState<TSingleProjectionPayload>,
                TSingleProjection>,
            SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>();
    public async Task<List<SingleProjectionState<TSingleProjectionPayload>>>
        GetSingleProjectionList<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>(
            QueryListType queryListType = QueryListType.ActiveOnly) where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : MultiProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>,
        new()
        where TSingleProjectionPayload : ISingleProjectionPayload
    {
        var projection = await GetSingleProjectionListObject<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>();
        return queryListType switch
        {
            QueryListType.ActiveAndDeleted => projection.Payload.List.ToList(),
            QueryListType.ActiveOnly => projection.Payload.List.Where(m => m.GetIsDeleted() == false).ToList(),
            QueryListType.DeletedOnly => projection.Payload.List.Where(m => m.GetIsDeleted()).ToList(),
            _ => projection.Payload.List.Where(m => m.GetIsDeleted() == false).ToList()
        };
    }
}
