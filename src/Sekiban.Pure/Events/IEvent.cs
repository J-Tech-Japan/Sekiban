using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Pure.Events;

public interface IEvent
{
    public int Version { get; }
    public string SortableUniqueId { get; }
    public PartitionKeys PartitionKeys { get; }
    public SortableUniqueIdValue GetSortableUniqueId() => new(SortableUniqueId);
    public IEventPayload GetPayload();
}
