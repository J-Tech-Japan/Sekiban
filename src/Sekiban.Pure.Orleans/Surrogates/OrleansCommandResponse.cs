using Sekiban.Pure.Events;
namespace Sekiban.Pure.Orleans.Surrogates;

[GenerateSerializer]
public record struct OrleansCommandResponse(
    [property: Id(0)] OrleansPartitionKeys PartitionKeys,
    [property: Id(1)] List<IEvent> Events,
    [property: Id(2)] int Version);