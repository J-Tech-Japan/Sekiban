using Sekiban.Pure.Events;
namespace Pure.Domain;

public record BranchCreated(string Name) : IEventPayload;
