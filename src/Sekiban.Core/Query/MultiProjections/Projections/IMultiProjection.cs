using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Core.Query.MultiProjections.Projections;

/// <summary>
///     Multi Projection Interface
/// </summary>
public interface IMultiProjection
{
    /// <summary>
    ///     Get Multi Projection
    ///     In Regular way ( can use cache / snapshots )
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <param name="includesSortableUniqueIdValue"></param>
    /// <typeparam name="TProjection"></typeparam>
    /// <typeparam name="TProjectionPayload"></typeparam>
    /// <returns></returns>
    Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionAsync<TProjection, TProjectionPayload>(
        string rootPartitionKey,
        SortableUniqueIdValue? includesSortableUniqueIdValue) where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();
    /// <summary>
    ///     Get Multi Projection from initial events
    ///     should not use cache / snapshots
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="includesSortableUniqueIdValue"></param>
    /// <typeparam name="TProjection"></typeparam>
    /// <typeparam name="TProjectionPayload"></typeparam>
    /// <returns></returns>
    Task<MultiProjectionState<TProjectionPayload>> GetInitialMultiProjectionFromStreamAsync<TProjection, TProjectionPayload>(
        Stream stream,
        string rootPartitionKey,
        SortableUniqueIdValue? includesSortableUniqueIdValue) where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();

    /// <summary>
    ///     Get Multi Projection from multiple streams
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="includesSortableUniqueIdValue"></param>
    /// <typeparam name="TProjection"></typeparam>
    /// <typeparam name="TProjectionPayload"></typeparam>
    /// <returns></returns>
    Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionFromMultipleStreamAsync<TProjection, TProjectionPayload>(
        Func<Task<Stream?>> stream,
        string rootPartitionKey,
        SortableUniqueIdValue? includesSortableUniqueIdValue) where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();
}
