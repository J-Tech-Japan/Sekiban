using Sekiban.Pure.Events;
namespace AspireEventSample.ApiService.Aggregates.Carts;

[GenerateSerializer]
public record ShoppingCartCreated(Guid UserId) : IEventPayload;