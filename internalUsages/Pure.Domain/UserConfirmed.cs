using Sekiban.Pure.Events;
namespace Pure.Domain;

[GenerateSerializer]
public record UserConfirmed : IEventPayload;
