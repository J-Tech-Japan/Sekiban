using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Cache;

public interface ISnapshotDocumentCache
{
    public void Set(SnapshotDocument document);
    public SnapshotDocument? Get(Guid aggregateId, Type originalAggregateType);
}
