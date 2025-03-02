using Sekiban.Pure.Aggregates;

namespace AspireEventSample.ApiService.Aggregates.Carts;
[GenerateSerializer]
public record PaymentProcessingShoppingCart(Guid UserId, List<ShoppingCartItems> Items, string PaymentMethod) : IAggregatePayload;