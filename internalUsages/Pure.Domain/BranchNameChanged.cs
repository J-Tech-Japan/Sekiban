using Sekiban.Pure.Events;
namespace Pure.Domain;

[GenerateSerializer]
public record BranchNameChanged(string Name) : IEventPayload;
