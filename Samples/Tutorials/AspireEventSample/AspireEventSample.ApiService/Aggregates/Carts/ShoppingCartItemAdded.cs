using Sekiban.Pure.Events;
namespace AspireEventSample.ApiService.Aggregates.Carts;

[GenerateSerializer]
public record ShoppingCartItemAdded(string Name, int Quantity, Guid ItemId, int Price) : IEventPayload;