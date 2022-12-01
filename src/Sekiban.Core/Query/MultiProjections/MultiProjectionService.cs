using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Query.SingleProjections;

namespace Sekiban.Core.Query.MultiProjections;

public class MultiProjectionService : IMultiProjectionService
{
    private readonly IMultiProjection multiProjection;

    public MultiProjectionService(IMultiProjection multiProjection)
    {
        this.multiProjection = multiProjection;
    }

    public Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionAsync<TProjectionPayload>()
        where TProjectionPayload : IMultiProjectionPayload, new()
    {
        return multiProjection.GetMultiProjectionAsync<MultiProjection<TProjectionPayload>, TProjectionPayload>();
    }

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
        GetSingleProjectionListObject<TSingleProjectionPayload>()
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        return multiProjection
            .GetMultiProjectionAsync<
                SingleProjectionListProjector<SingleProjection<TSingleProjectionPayload>,
                    SingleProjectionState<TSingleProjectionPayload>,
                    SingleProjection<TSingleProjectionPayload>>,
                SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>();
    }

    public async Task<List<SingleProjectionState<TSingleProjectionPayload>>>
        GetSingleProjectionList<TSingleProjectionPayload>(
            QueryListType queryListType = QueryListType.ActiveOnly)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        var projection = await GetSingleProjectionListObject<TSingleProjectionPayload>();
        return queryListType switch
        {
            QueryListType.ActiveAndDeleted => projection.Payload.List.ToList(),
            QueryListType.ActiveOnly => projection.Payload.List.Where(m => m.GetIsDeleted() == false).ToList(),
            QueryListType.DeletedOnly => projection.Payload.List.Where(m => m.GetIsDeleted()).ToList(),
            _ => projection.Payload.List.Where(m => m.GetIsDeleted() == false).ToList()
        };
    }
}
