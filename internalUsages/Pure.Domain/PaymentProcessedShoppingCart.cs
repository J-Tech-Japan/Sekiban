using Sekiban.Pure.Events;
namespace Pure.Domain;

[GenerateSerializer]
public record PaymentProcessedShoppingCart(string PaymentMethod) : IEventPayload;
