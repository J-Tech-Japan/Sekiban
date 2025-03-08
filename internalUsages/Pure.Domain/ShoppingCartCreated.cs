using Sekiban.Pure.Events;
namespace Pure.Domain;

[GenerateSerializer]
public record ShoppingCartCreated(Guid UserId) : IEventPayload;
