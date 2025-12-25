using Sekiban.Pure.Events;
namespace Pure.Domain;

[GenerateSerializer]
public record UserUnconfirmed : IEventPayload;
