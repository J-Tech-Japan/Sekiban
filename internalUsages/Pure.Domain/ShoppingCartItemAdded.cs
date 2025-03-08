using Sekiban.Pure.Events;
namespace Pure.Domain;

[GenerateSerializer]
public record ShoppingCartItemAdded(string Name, int Quantity, Guid ItemId, int Price) : IEventPayload;
