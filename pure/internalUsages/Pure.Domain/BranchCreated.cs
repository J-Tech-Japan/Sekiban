using Sekiban.Pure.Events;
namespace Pure.Domain;

[GenerateSerializer]
public record BranchCreated(string Name) : IEventPayload;
