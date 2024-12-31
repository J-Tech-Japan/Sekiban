using Sekiban.Pure.Events;
namespace Pure.Domain;

public record ShoppingCartCreated(Guid UserId) : IEventPayload;
