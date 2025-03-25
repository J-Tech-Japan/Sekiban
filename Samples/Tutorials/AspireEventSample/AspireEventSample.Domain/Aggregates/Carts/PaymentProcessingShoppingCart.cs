using Orleans;
using Sekiban.Pure.Aggregates;
namespace AspireEventSample.Domain.Aggregates.Carts;

[GenerateSerializer]
public record PaymentProcessingShoppingCart(Guid UserId, List<ShoppingCartItems> Items, string PaymentMethod)
    : IAggregatePayload;