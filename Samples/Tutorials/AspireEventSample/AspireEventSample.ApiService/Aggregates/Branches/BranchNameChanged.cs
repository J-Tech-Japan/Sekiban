using Sekiban.Pure.Events;

namespace AspireEventSample.ApiService.Aggregates.Branches;

[GenerateSerializer]
public record BranchNameChanged(string Name) : IEventPayload;