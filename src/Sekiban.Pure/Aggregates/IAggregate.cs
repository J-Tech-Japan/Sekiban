using ResultBoxes;
using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Pure;

public interface IAggregate
{
    public int Version { get; }
    public string LastSortableUniqueId { get; }
    public PartitionKeys PartitionKeys { get; }
    public OptionalValue<SortableUniqueIdValue> GetLastSortableUniqueIdValue() =>
        SortableUniqueIdValue.OptionalValue(LastSortableUniqueId);
    public IAggregatePayload GetPayload();
}
