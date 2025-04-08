using Orleans;
using Sekiban.Pure.Events;
namespace AspireEventSample.Domain.Aggregates.Branches;

[GenerateSerializer]
public record BranchCreated([property: Id(0)] string Name, [property: Id(1)] string Country) : IEventPayload;