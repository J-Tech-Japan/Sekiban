using Orleans;
using Sekiban.Pure.Aggregates;
namespace AspireEventSample.Domain.Aggregates.Carts;

[GenerateSerializer]
public record BuyingShoppingCart(Guid UserId, List<ShoppingCartItems> Items) : IAggregatePayload;