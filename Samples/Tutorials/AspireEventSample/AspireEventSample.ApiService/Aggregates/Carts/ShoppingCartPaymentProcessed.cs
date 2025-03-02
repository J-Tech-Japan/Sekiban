using Sekiban.Pure.Events;
namespace AspireEventSample.ApiService.Aggregates.Carts;

[GenerateSerializer]
public record ShoppingCartPaymentProcessed(string PaymentMethod) : IEventPayload;