using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.MultiProjections;

/// <summary>
///     Multi Projection Retrieve Service.
/// </summary>
public class MultiProjectionService : IMultiProjectionService
{
    private readonly IMultiProjection multiProjection;

    public MultiProjectionService(IMultiProjection multiProjection) => this.multiProjection = multiProjection;

    public Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionAsync<TProjectionPayload>(
        string rootPartitionKey,
        SortableUniqueIdValue? includesSortableUniqueIdValue) where TProjectionPayload : IMultiProjectionPayloadCommon =>
        multiProjection.GetMultiProjectionAsync<MultiProjection<TProjectionPayload>, TProjectionPayload>(
            rootPartitionKey,
            includesSortableUniqueIdValue);

    public async Task<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>> GetAggregateListObject<TAggregatePayload>(
        string rootPartitionKey,
        SortableUniqueIdValue? includesSortableUniqueIdValue) where TAggregatePayload : IAggregatePayloadCommon
    {
        var list = await multiProjection
            .GetMultiProjectionAsync<
                SingleProjectionListProjector<Aggregate<TAggregatePayload>, AggregateState<TAggregatePayload>,
                    DefaultSingleProjector<TAggregatePayload>>, SingleProjectionListState<AggregateState<TAggregatePayload>>>(
                rootPartitionKey,
                includesSortableUniqueIdValue);
        return list;
    }

    public async Task<List<AggregateState<TAggregatePayload>>> GetAggregateList<TAggregatePayload>(
        QueryListType queryListType = QueryListType.ActiveOnly,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions,
        SortableUniqueIdValue? includesSortableUniqueIdValue = null) where TAggregatePayload : IAggregatePayloadCommon
    {
        var projection = await GetAggregateListObject<TAggregatePayload>(rootPartitionKey, includesSortableUniqueIdValue);
        return queryListType switch
        {
            QueryListType.ActiveAndDeleted => [.. projection.Payload.List],
            QueryListType.ActiveOnly => projection.Payload.List.Where(m => m.GetIsDeleted() == false).ToList(),
            QueryListType.DeletedOnly => projection.Payload.List.Where(m => m.GetIsDeleted()).ToList(),
            _ => projection.Payload.List.Where(m => m.GetIsDeleted() == false).ToList()
        };
    }

    public Task<MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>
        GetSingleProjectionListObject<TSingleProjectionPayload>(string rootPartitionKey, SortableUniqueIdValue? includesSortableUniqueIdValue)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        multiProjection
            .GetMultiProjectionAsync<
                SingleProjectionListProjector<SingleProjection<TSingleProjectionPayload>, SingleProjectionState<TSingleProjectionPayload>,
                    SingleProjection<TSingleProjectionPayload>>, SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>(
                rootPartitionKey,
                includesSortableUniqueIdValue);

    public async Task<List<SingleProjectionState<TSingleProjectionPayload>>> GetSingleProjectionList<TSingleProjectionPayload>(
        QueryListType queryListType = QueryListType.ActiveOnly,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions,
        SortableUniqueIdValue? includesSortableUniqueIdValue = null) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon
    {
        var projection = await GetSingleProjectionListObject<TSingleProjectionPayload>(rootPartitionKey, includesSortableUniqueIdValue);
        return queryListType switch
        {
            QueryListType.ActiveAndDeleted => [.. projection.Payload.List],
            QueryListType.ActiveOnly => projection.Payload.List.Where(m => m.GetIsDeleted() == false).ToList(),
            QueryListType.DeletedOnly => projection.Payload.List.Where(m => m.GetIsDeleted()).ToList(),
            _ => projection.Payload.List.Where(m => m.GetIsDeleted() == false).ToList()
        };
    }
}
