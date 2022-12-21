using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Cache;

/// <summary>
///     defines a interface for snapshot document Cache
///     Default implementation is <see cref="SnapshotDocumentCache" />
///     Application developer can implement this interface to provide custom cache implementation
/// </summary>
public interface ISnapshotDocumentCache
{
    public void Set(SnapshotDocument document);
    public SnapshotDocument? Get(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType);
}
