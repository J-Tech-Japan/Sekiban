using Sekiban.Pure.Events;
namespace Pure.Domain;

[GenerateSerializer]
public record UserRegistered(string Name, string Email) : IEventPayload;
