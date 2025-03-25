using Orleans;
using Sekiban.Pure.Events;
namespace AspireEventSample.Domain.Aggregates.Branches;

[GenerateSerializer]
public record BranchNameChanged(string Name) : IEventPayload;