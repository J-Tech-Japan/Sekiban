using Sekiban.Pure.Events;
namespace Pure.Domain;

public record BranchNameChanged(string Name) : IEventPayload;
