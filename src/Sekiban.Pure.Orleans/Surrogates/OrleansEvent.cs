using Sekiban.Pure.Events;
namespace Sekiban.Pure.Orleans.Surrogates;

[GenerateSerializer]
public record struct OrleansEvent<TEventPayload>(
    [property: Id(0)] Guid Id,
    [property: Id(1)] TEventPayload Payload,
    [property: Id(2)] OrleansPartitionKeys PartitionKeys,
    [property: Id(3)] string SortableUniqueId,
    [property: Id(4)] int Version,
    [property: Id(5)] OrleansEventMetadata Metadata) where TEventPayload : IEventPayload;