using Sekiban.Pure.Events;
namespace Pure.Domain;

public record ShoppingCartItemAdded(string Name, int Quantity) : IEventPayload;
