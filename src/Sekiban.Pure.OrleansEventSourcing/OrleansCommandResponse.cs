namespace Sekiban.Pure.OrleansEventSourcing;

[GenerateSerializer]
public record OrleansCommandResponse([property:Id(0)]OrleansPartitionKeys PartitionKeys, [property:Id(1)]List<string> Events, [property:Id(2)]int Version)
{
}