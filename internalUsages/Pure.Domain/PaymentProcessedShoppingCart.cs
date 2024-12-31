using Sekiban.Pure.Events;
namespace Pure.Domain;

public record PaymentProcessedShoppingCart(string PaymentMethod) : IEventPayload;
