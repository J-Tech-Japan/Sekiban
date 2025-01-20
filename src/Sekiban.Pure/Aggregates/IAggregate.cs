using ResultBoxes;
using Sekiban.Pure.Documents;
namespace Sekiban.Pure.Aggregates;

public interface IAggregate
{
    public int Version { get; }
    public string LastSortableUniqueId { get; }
    public PartitionKeys PartitionKeys { get; }
    public OptionalValue<SortableUniqueIdValue> GetLastSortableUniqueIdValue() =>
        SortableUniqueIdValue.OptionalValue(LastSortableUniqueId);
    public IAggregatePayload GetPayload();
    public ResultBox<Aggregate<TAggregatePayload>> ToTypedPayload<TAggregatePayload>() where TAggregatePayload : IAggregatePayload;

}
