using Sekiban.Pure.Events;
namespace Pure.Domain;

[GenerateSerializer]
public record ClientNameChanged(string Name) : IEventPayload;
