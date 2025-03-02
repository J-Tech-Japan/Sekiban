using Sekiban.Pure.Aggregates;

namespace AspireEventSample.ApiService.Aggregates.Carts;
[GenerateSerializer]
public record BuyingShoppingCart(Guid UserId, List<ShoppingCartItems> Items) : IAggregatePayload;