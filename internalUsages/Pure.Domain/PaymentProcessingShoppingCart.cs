using Sekiban.Pure;
namespace Pure.Domain;

public record PaymentProcessingShoppingCart(
    Guid UserId,
    List<ShoppingCartItems> Items,
    int TotalPrice,
    string PaymentMethod) : IAggregatePayload;
