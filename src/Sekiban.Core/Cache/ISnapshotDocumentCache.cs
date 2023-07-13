using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Cache;

/// <summary>
///     defines a interface for snapshot document Cache
///     Default implementation is <see cref="SnapshotDocumentCache" />
///     Application developer can implement this interface to provide custom cache implementation
/// </summary>
public interface ISnapshotDocumentCache
{
    /// <summary>
    ///     Set snapshot document to cache
    /// </summary>
    /// <param name="document"></param>
    public void Set(SnapshotDocument document);
    /// <summary>
    ///     Get snapshot document from cache
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="aggregatePayloadType"></param>
    /// <param name="projectionPayloadType"></param>
    /// <param name="rootPartitionKey"></param>
    /// <returns></returns>
    public SnapshotDocument? Get(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, string rootPartitionKey);
}
