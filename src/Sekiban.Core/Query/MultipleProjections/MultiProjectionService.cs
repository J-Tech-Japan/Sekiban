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
    public async Task<MultiProjectionState<SingleProjectionListState<AggregateIdentifierState<TAggregatePayload>>>>
        GetAggregateListObject<TAggregatePayload>() where TAggregatePayload : IAggregatePayload, new()
    {
        var list = await _multipleProjection
            .GetMultipleProjectionAsync<SingleProjectionListProjector<AggregateIdentifier<TAggregatePayload>,
                    AggregateIdentifierState<TAggregatePayload>,
                    DefaultSingleProjector<TAggregatePayload>>
                , SingleProjectionListState<AggregateIdentifierState<TAggregatePayload>>>();
        return list;
    }
    public async Task<List<AggregateIdentifierState<TAggregatePayload>>> GetAggregateList<TAggregatePayload>(
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
        where TSingleProjectionPayload : ISingleProjectionPayload => _multipleProjection
        .GetMultipleProjectionAsync<
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
