namespace Sekiban.Core.Query.SingleProjections.Projections;

public interface ISingleProjectionFromInitial
{
    /// <summary>
    ///     Creates an Aggregate from the initial event without using the memory cache.
    ///     It's slow, so please normally use the cached version.
    ///     This remains for testing and verification purposes.
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="toVersion"></param>
    /// <typeparam name="TProjection"></typeparam>
    /// <typeparam name="TProjector"></typeparam>
    /// <returns></returns>
    Task<TProjection?> GetAggregateFromInitialAsync<TProjection, TProjector>(Guid aggregateId, string rootPartitionKey, int? toVersion)
        where TProjection : IAggregateCommon, SingleProjections.ISingleProjection where TProjector : ISingleProjector<TProjection>, new();
}
