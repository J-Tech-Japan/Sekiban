using Sekiban.Pure.Events;
namespace Pure.Domain;

public record UserRegistered(string Name, string Email) : IEventPayload;
