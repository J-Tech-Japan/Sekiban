using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Documents;

namespace Sekiban.Pure.OrleansEventSourcing;

public class Class1
{
}

[GenerateSerializer]
public record OrleansCommandResponse([property:Id(0)]OrleansPartitionKeys PartitionKeys, [property:Id(1)]List<string> Events, [property:Id(2)]int Version)
{
}

[GenerateSerializer]
public record OrleansPartitionKeys(
    [property: Id(0)] Guid AggregateId,
    [property: Id(1)] string Group,
    [property: Id(2)] string RootPartitionKey);


[GenerateSerializer]
public record OrleansCommand([property:Id(0)]string payload);

public static class PartitionKeysExtensions
{
    public static OrleansPartitionKeys ToOrleansPartitionKeys(this PartitionKeys partitionKeys) =>
        new(partitionKeys.AggregateId, partitionKeys.Group, partitionKeys.RootPartitionKey);
}

public static class CommandResponseExtensions
{
    public static OrleansCommandResponse ToOrleansCommandResponse(this CommandResponse response) =>
        new(response.PartitionKeys.ToOrleansPartitionKeys(), response.Events.Select(e => e.ToString() ?? String.Empty).ToList(), response.Version);
}
