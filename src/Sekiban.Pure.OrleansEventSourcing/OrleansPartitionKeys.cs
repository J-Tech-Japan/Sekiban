using Sekiban.Pure.Documents;

namespace Sekiban.Pure.OrleansEventSourcing;

[GenerateSerializer]
public record OrleansPartitionKeys(
    [property: Id(0)] Guid AggregateId,
    [property: Id(1)] string Group,
    [property: Id(2)] string RootPartitionKey)
{
    public PartitionKeys ToPartitionKeys() => new(AggregateId, Group, RootPartitionKey);
}