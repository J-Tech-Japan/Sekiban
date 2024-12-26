using Sekiban.Core.Aggregate;
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
        MultiProjectionRetrievalOptions? retrievalOptions) where TProjectionPayload : IMultiProjectionPayloadCommon =>
        multiProjection.GetMultiProjectionAsync<MultiProjection<TProjectionPayload>, TProjectionPayload>(
            rootPartitionKey,
            retrievalOptions);

    public async Task<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>>
        GetAggregateListObject<TAggregatePayload>(
            string rootPartitionKey,
            MultiProjectionRetrievalOptions? retrievalOptions) where TAggregatePayload : IAggregatePayloadCommon
    {
        var list = await multiProjection
            .GetMultiProjectionAsync<
                SingleProjectionListProjector<Aggregate<TAggregatePayload>, AggregateState<TAggregatePayload>,
                    DefaultSingleProjector<TAggregatePayload>>,
                SingleProjectionListState<AggregateState<TAggregatePayload>>>(rootPartitionKey, retrievalOptions);
        return list;
    }

    public async Task<List<AggregateState<TAggregatePayload>>> GetAggregateList<TAggregatePayload>(
        QueryListType queryListType = QueryListType.ActiveOnly,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions,
        MultiProjectionRetrievalOptions? retrievalOptions = null) where TAggregatePayload : IAggregatePayloadCommon
    {
        var projection = await GetAggregateListObject<TAggregatePayload>(rootPartitionKey, retrievalOptions);
        return queryListType switch
        {
            QueryListType.ActiveAndDeleted => [.. projection.Payload.List],
            QueryListType.ActiveOnly => projection.Payload.List.Where(m => m.GetIsDeleted() == false).ToList(),
            QueryListType.DeletedOnly => projection.Payload.List.Where(m => m.GetIsDeleted()).ToList(),
            _ => projection.Payload.List.Where(m => m.GetIsDeleted() == false).ToList()
        };
    }

    public Task<MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>
        GetSingleProjectionListObject<TSingleProjectionPayload>(
            string rootPartitionKey,
            MultiProjectionRetrievalOptions? retrievalOptions)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        multiProjection
            .GetMultiProjectionAsync<
                SingleProjectionListProjector<SingleProjection<TSingleProjectionPayload>,
                    SingleProjectionState<TSingleProjectionPayload>, SingleProjection<TSingleProjectionPayload>>,
                SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>(
                rootPartitionKey,
                retrievalOptions);

    public async Task<List<SingleProjectionState<TSingleProjectionPayload>>>
        GetSingleProjectionList<TSingleProjectionPayload>(
            QueryListType queryListType = QueryListType.ActiveOnly,
            string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions,
            MultiProjectionRetrievalOptions? retrievalOptions = null)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon
    {
        var projection
            = await GetSingleProjectionListObject<TSingleProjectionPayload>(rootPartitionKey, retrievalOptions);
        return queryListType switch
        {
            QueryListType.ActiveAndDeleted => [.. projection.Payload.List],
            QueryListType.ActiveOnly => projection.Payload.List.Where(m => m.GetIsDeleted() == false).ToList(),
            QueryListType.DeletedOnly => projection.Payload.List.Where(m => m.GetIsDeleted()).ToList(),
            _ => projection.Payload.List.Where(m => m.GetIsDeleted() == false).ToList()
        };
    }
}
