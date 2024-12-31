using Sekiban.Pure.Aggregates;
namespace Pure.Domain;

public record BuyingShoppingCart(Guid UserId, List<ShoppingCartItems> Items) : IAggregatePayload;
